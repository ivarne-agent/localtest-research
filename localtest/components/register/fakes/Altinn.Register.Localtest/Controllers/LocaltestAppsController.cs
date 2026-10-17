using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// Registers apps whose <c>testData.json</c> is merged into the register (the old localtest read it from every
/// running app). Every call re-fetches the test data of all registered apps and swaps in a rebuilt register.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("register/api/localtest/apps")]
public sealed class LocaltestAppsController(RegisterStoreProvider provider)
    : ControllerBase
{
    [HttpGet]
    public ActionResult<AppsResponse> Get()
        => Ok(AppsResponse.From(provider.State));

    /// <summary>Registers an app (idempotent) and rebuilds the register.</summary>
    [HttpPost]
    public async Task<ActionResult<AppsResponse>> Register([FromBody] AppRegistration body, CancellationToken cancellationToken)
    {
        if (!IsName(body.Org) || !IsName(body.App))
        {
            return Problem(title: "org and app are required: lower-case letters, digits and dashes", statusCode: 400);
        }

        return Ok(AppsResponse.From(await provider.Register(body.Org!, body.App!, cancellationToken)));
    }

    [HttpDelete("{org}/{app}")]
    public async Task<ActionResult<AppsResponse>> Unregister(string org, string app, CancellationToken cancellationToken)
        => await provider.Unregister(org, app, cancellationToken) is { } state
            ? Ok(AppsResponse.From(state))
            : NotFound();

    /// <summary>Re-reads the seed and re-fetches every registered app's test data.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AppsResponse>> Refresh(CancellationToken cancellationToken)
        => Ok(AppsResponse.From(await provider.Refresh(cancellationToken)));

    private static bool IsName(string? value)
        => !string.IsNullOrEmpty(value) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public sealed record AppRegistration(string? Org, string? App);

    /// <param name="Source"><c>seed+apps</c> when the seed users are included, <c>apps</c> when every registered app serves test data.</param>
    public sealed record AppsResponse(string Source, IReadOnlyList<AppDto> Apps, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Warnings, int Users, int Parties)
    {
        public static AppsResponse From(RegistrationState state)
            => new(
                state.Apps.Count > 0 && state.Apps.All(a => a.Data is not null) ? "apps" : "seed+apps",
                [.. state.Apps.Select(a => new AppDto(a.Org, a.App, a.Status.ToString(), a.Data?.Persons.Count ?? 0, a.Data?.Orgs.Count ?? 0, a.FetchedAt, a.Error))],
                state.Conflicts,
                state.Warnings,
                state.Users,
                state.Parties);
    }

    public sealed record AppDto(string Org, string App, string Status, int Persons, int Orgs, DateTimeOffset? FetchedAt, string? Error);
}
