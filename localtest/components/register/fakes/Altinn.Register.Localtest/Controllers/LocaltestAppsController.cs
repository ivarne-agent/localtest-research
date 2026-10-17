using Altinn.Register.Localtest.Persistence;
using Altinn.Register.Localtest.TestData;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// The app runner's hook on the Register: <c>PUT register/api/localtest/apps/{org}/{app}</c> with the app's
/// <c>App/wwwroot/testData.json</c> as the body (or an empty body for an app without test data) registers the app
/// and rebuilds the store. The Register never fetches anything from an app.
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

    [HttpPut("{org}/{app}")]
    public async Task<ActionResult<AppsResponse>> Put(string org, string app, CancellationToken cancellationToken)
    {
        if (!IsName(org) || !IsName(app))
        {
            return Problem(title: "org and app are required: lower-case letters, digits, dashes and underscores", statusCode: 400);
        }

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        TestDataFile? data = null;
        if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "null")
        {
            try
            {
                data = TestDataFile.Parse(json, $"app {org}/{app}");
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
            {
                return Problem(title: "testData.json could not be read", detail: e.Message, statusCode: 400);
            }
        }

        return Ok(AppsResponse.From(await provider.Register(org, app, data, cancellationToken)));
    }

    [HttpDelete("{org}/{app}")]
    public async Task<ActionResult<AppsResponse>> Unregister(string org, string app, CancellationToken cancellationToken)
        => await provider.Unregister(org, app, cancellationToken) is { } state
            ? Ok(AppsResponse.From(state))
            : NotFound();

    /// <summary>Re-reads the built-in seed and rebuilds the store with the posted app data.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AppsResponse>> Refresh(CancellationToken cancellationToken)
        => Ok(AppsResponse.From(await provider.Refresh(cancellationToken)));

    private static bool IsName(string? value)
        => !string.IsNullOrEmpty(value) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public sealed record AppsResponse(string Source, IReadOnlyList<AppDto> Apps, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Warnings, int Users, int Parties)
    {
        public static AppsResponse From(RegistrationState state)
            => new(
                state.Apps.Count > 0 && state.Apps.All(a => a.Data is not null) ? "apps" : "seed+apps",
                [.. state.Apps.Select(a => new AppDto(a.Org, a.App, a.Status.ToString(), a.Data?.Persons.Count ?? 0, a.Data?.Orgs.Count ?? 0, a.ReceivedAt))],
                state.Conflicts,
                state.Warnings,
                state.Users,
                state.Parties);
    }

    public sealed record AppDto(string Org, string App, string Status, int Persons, int Orgs, DateTimeOffset ReceivedAt);
}
