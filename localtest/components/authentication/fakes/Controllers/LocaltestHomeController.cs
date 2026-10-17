#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Localtest.Identity;
using Altinn.Platform.Authentication.Localtest.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Controllers;

/// <summary>
/// The old localtest's start page and login contract, which the app-frontend Cypress suite and app developers use:
/// <c>GET /</c> (user select), <c>POST /Home/LogInTestUser</c> and <c>GET /Home/Localtest/Version</c>.
/// The login runs the production sign-in headlessly (<see cref="LocaltestSignIn"/>).
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class LocaltestHomeController(
    RegisterLocaltestClient register,
    LocaltestSignIn signIn,
    IOptions<GeneralSettings> generalSettings) : ControllerBase
{
    /// <summary>
    /// The localtest API version apps check through <c>LocaltestValidation</c> (they require 6 or later).
    /// 8 matches the old localtest at the time this component replaced it.
    /// </summary>
    public const int LocaltestApiVersion = 8;

    [HttpGet("/")]
    [HttpGet("/Home")]
    [HttpGet("/Home/Index")]
    public async Task<IActionResult> Index([FromQuery(Name = "goto")] string? goTo, [FromQuery] string? app, CancellationToken cancellationToken)
    {
        var users = await register.GetTestUsers(cancellationToken);
        var html = LocaltestLoginPage.Render(users, new LocaltestLoginPage.Options(
            Title: "Altinn localtest",
            Intro: "Log in as a test user. The production Altinn Authentication signs the user in; the selected party is set in the party cookies.",
            FormAction: "/Home/LogInTestUser",
            Hidden: new Dictionary<string, string?> { ["action"] = "start", ["RedirectUrl"] = goTo },
            SelectedLevel: LocaltestAuthLevels.Default,
            IncludeOtherParties: true,
            ShowAppPath: true,
            AppPath: app,
            Notice: null));
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("/Home/Localtest/Version")]
    public IActionResult Version() => Content(LocaltestApiVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), "application/json");

    /// <summary>
    /// Form (or JSON) fields: <c>UserSelect</c> = <c>userId.partyId</c>, <c>AuthenticationLevel</c> 0-4 (-1 skips the
    /// login), <c>action</c> (<c>reauthenticate</c> answers 204 with the cookies; anything else redirects),
    /// <c>RedirectUrl</c>, <c>AppPathSelection</c> (<c>org/app</c>).
    /// </summary>
    [HttpPost("/Home/LogInTestUser")]
    public async Task<IActionResult> LogInTestUser(CancellationToken cancellationToken)
    {
        var fields = await ReadFields(cancellationToken);
        string? Field(string name) => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        if (Field("AuthenticationLevel") != "-1")
        {
            if (!LocaltestUserSelect.TryParse(Field("UserSelect"), out var userId, out var partyId))
            {
                return BadRequest("UserSelect must be userId.partyId.");
            }

            var level = Field("AuthenticationLevel") is { } requested
                ? LocaltestAuthLevels.Parse(requested)
                : LocaltestAuthLevels.ForLevel(LocaltestAuthLevels.Default);
            if (level is null)
            {
                return BadRequest("AuthenticationLevel must be 0, 1, 2, 3 or 4.");
            }

            var user = (await register.GetTestUsers(cancellationToken)).FirstOrDefault(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound($"No test user with userId {userId}.");
            }

            var party = user.FindParty(partyId ?? user.PartyId);
            if (party is null)
            {
                return BadRequest($"Test user {userId} cannot act for party {partyId}.");
            }

            var (result, failure) = await signIn.SignIn(user, level, HttpContext.Connection.RemoteIpAddress, cancellationToken);
            if (failure is not null)
            {
                return StatusCode(failure.StatusCode, failure.Message);
            }

            signIn.AppendCookies(Response, result!, party.Value.PartyId, party.Value.PartyUuid);
        }

        if (string.Equals(Field("action"), "reauthenticate", StringComparison.OrdinalIgnoreCase))
        {
            return NoContent();
        }

        if (Field("RedirectUrl") is { } redirectUrl && IsOwnOrigin(redirectUrl))
        {
            return Redirect(redirectUrl);
        }

        return Field("AppPathSelection") is { } app ? Redirect($"/{app.Trim('/')}/") : Redirect("/");
    }

    private async Task<Dictionary<string, string?>> ReadFields(CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var (key, value) in form)
            {
                fields[key] = value.ToString();
            }
        }
        else if (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Null or JsonValueKind.Undefined => null,
                        _ => property.Value.GetRawText(),
                    };
                }
            }
        }

        return fields;
    }

    /// <summary>Relative URLs and URLs on the platform origin (where the apps run too); no open redirect.</summary>
    private bool IsOwnOrigin(string url)
    {
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && Uri.TryCreate(generalSettings.Value.BaseUrl, UriKind.Absolute, out var platform)
            && Uri.Compare(absolute, platform, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
