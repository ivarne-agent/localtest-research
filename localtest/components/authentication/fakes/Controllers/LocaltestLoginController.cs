#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Localtest.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Controllers;

/// <summary>
/// The test-user picker. Production redirects the browser to the upstream provider's authorization endpoint;
/// locally that endpoint is this page. Submitting it issues a one-time code and sends the browser straight to the
/// production <c>upstream/callback</c>, where the real sign-in continues.
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
    private static readonly (string Acr, string Label)[] _levels =
    [
        ("idporten-loa-substantial", "Level 3 (substantial)"),
        ("idporten-loa-high", "Level 4 (high)"),
    ];

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
        var requested = acrValues?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var selectedAcr = _levels.Select(l => l.Acr).FirstOrDefault(requested.Contains) ?? options.Value.DefaultAcr;

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Localtest login</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px}")
            .Append("form{border:1px solid #ccc;border-radius:6px;padding:12px 16px;margin:10px 0}")
            .Append("button{padding:6px 14px}small{color:#555}</style></head><body>")
            .Append("<h1>Pick a test user</h1><p>Production Altinn Authentication running locally. Choosing a user completes the sign-in through the real upstream callback.</p>");

        foreach (var user in users)
        {
            html.Append("<form method=\"post\" action=\"login\">")
                .Append(Hidden("state", state)).Append(Hidden("nonce", nonce)).Append(Hidden("redirect_uri", redirectUri)).Append(Hidden("ssn", user.Ssn)).Append(Hidden("userId", user.UserId.ToString()))
                .Append("<strong>").Append(WebUtility.HtmlEncode(user.DisplayName)).Append("</strong> <small>")
                .Append(WebUtility.HtmlEncode(user.Ssn)).Append(" · userId ").Append(user.UserId).Append(" · partyId ").Append(user.PartyId).Append("</small>");

            if (user.Parties.Count > 0)
            {
                html.Append("<br><small>").Append(WebUtility.HtmlEncode(string.Join("; ", user.Parties.Select(p => $"{p.DisplayName} ({string.Join(", ", p.Roles)})")))).Append("</small>");
            }

            html.Append("<div style=\"margin-top:6px\"><select name=\"acr\">");
            foreach (var (acr, label) in _levels)
            {
                html.Append("<option value=\"").Append(acr).Append('"').Append(acr == selectedAcr ? " selected" : string.Empty).Append('>').Append(label).Append("</option>");
            }

            html.Append("</select> <button type=\"submit\">Log in</button></div></form>");
        }

        if (users.Count == 0)
        {
            html.Append("<p><em>No test users: is the register component running at ").Append(WebUtility.HtmlEncode(options.Value.RegisterLocaltestEndpoint)).Append("?</em></p>");
        }

        html.Append("</body></html>");
        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    [HttpPost("login")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult Pick(
        [FromForm] string state,
        [FromForm] string? nonce,
        [FromForm(Name = "redirect_uri")] string? redirectUri,
        [FromForm] string? ssn,
        [FromForm] int? userId,
        [FromForm] string? acr)
    {
        var code = tickets.Issue(string.IsNullOrEmpty(ssn) ? null : ssn, userId, string.IsNullOrEmpty(acr) ? options.Value.DefaultAcr : acr, nonce);
        var callback = string.IsNullOrEmpty(redirectUri)
            ? new Uri(new Uri(generalSettings.Value.PlatformEndpoint), "authentication/api/v1/upstream/callback").ToString()
            : redirectUri;
        return Redirect(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?> { ["code"] = code, ["state"] = state }));
    }

    private static string Hidden(string name, string? value)
        => $"<input type=\"hidden\" name=\"{name}\" value=\"{WebUtility.HtmlEncode(value ?? string.Empty)}\">";
}
