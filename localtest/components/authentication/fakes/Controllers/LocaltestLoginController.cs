#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Localtest.Identity;
using Altinn.Platform.Authentication.Localtest.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Controllers;

/// <summary>
/// The test-user picker. Production redirects the browser to the upstream provider's authorization endpoint;
/// locally that endpoint is this page. Submitting it issues a one-time code and sends the browser straight to the
/// production <c>upstream/callback</c>, where the real sign-in continues. Only each user's own party is offered:
/// the production callback sets the party cookies, and the app's party selection handles acting for others.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("authentication/api/v1/localtest")]
public sealed class LocaltestLoginController(
    RegisterLocaltestClient register,
    LocaltestTicketStore tickets,
    IOptions<GeneralSettings> generalSettings,
    IOptions<LocaltestAuthenticationOptions> options) : ControllerBase
{
    [HttpGet("login")]
    public async Task<IActionResult> Login(
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "acr_values")] string? acrValues,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state))
        {
            return BadRequest("Missing state. Start the login from an app or from authentication/api/v1/authentication?goTo=...");
        }

        var users = await register.GetTestUsers(cancellationToken);

        // Preselect the highest level the app asked for, else the configured default.
        var requested = (acrValues?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(LocaltestAuthLevels.ForAcr)
            .OfType<LocaltestAuthLevels.Entry>()
            .Select(e => (int?)e.Level)
            .Max();
        var selected = requested ?? LocaltestAuthLevels.ForAcr(options.Value.DefaultAcr)?.Level ?? LocaltestAuthLevels.Default;

        var html = LocaltestLoginPage.Render(users, new LocaltestLoginPage.Options(
            Title: "Pick a test user",
            Intro: "Production Altinn Authentication running locally. Choosing a user completes the sign-in through the real upstream callback.",
            FormAction: "login",
            Hidden: new Dictionary<string, string?> { ["state"] = state, ["nonce"] = nonce, ["redirect_uri"] = redirectUri },
            SelectedLevel: selected,
            IncludeOtherParties: false,
            ShowAppPath: false,
            AppPath: null,
            Notice: users.Count == 0 ? $"Register endpoint: {options.Value.RegisterLocaltestEndpoint}" : null));
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("login")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Pick(
        [FromForm] string state,
        [FromForm] string? nonce,
        [FromForm(Name = "redirect_uri")] string? redirectUri,
        [FromForm(Name = "UserSelect")] string? userSelect,
        [FromForm(Name = "AuthenticationLevel")] string? authenticationLevel,
        CancellationToken cancellationToken)
    {
        if (!LocaltestUserSelect.TryParse(userSelect, out var userId, out _))
        {
            return BadRequest("UserSelect must be userId.partyId.");
        }

        var user = (await register.GetTestUsers(cancellationToken)).FirstOrDefault(u => u.UserId == userId);
        if (user is null)
        {
            return NotFound($"No test user with userId {userId}.");
        }

        var level = LocaltestAuthLevels.Parse(authenticationLevel) ?? LocaltestAuthLevels.ForAcr(options.Value.DefaultAcr) ?? LocaltestAuthLevels.ForLevel(LocaltestAuthLevels.Default)!;
        var code = tickets.Issue(string.IsNullOrEmpty(user.Ssn) ? null : user.Ssn, user.UserId, level.Acr, nonce);
        var callback = string.IsNullOrEmpty(redirectUri)
            ? new Uri(new Uri(generalSettings.Value.PlatformEndpoint), "authentication/api/v1/upstream/callback").ToString()
            : redirectUri;
        return Redirect(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?> { ["code"] = code, ["state"] = state }));
    }
}
