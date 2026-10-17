#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Text;
using Altinn.Platform.Authentication.Localtest.Identity;

namespace Altinn.Platform.Authentication.Localtest.Login;

/// <summary>
/// The login form shared by the start page (<c>/</c>, posting to <c>/Home/LogInTestUser</c>) and the upstream picker
/// (<c>authentication/api/v1/localtest/login</c>). Field names follow the old localtest's start page, which tests
/// parse: <c>&lt;select id="UserSelect"&gt;</c> with <c>userId.partyId</c> values, <c>AuthenticationLevel</c>, <c>AppPathSelection</c>.
/// </summary>
public static class LocaltestLoginPage
{
    public sealed record Options(
        string Title,
        string Intro,
        string FormAction,
        IReadOnlyDictionary<string, string?> Hidden,
        int SelectedLevel,
        bool IncludeOtherParties,
        bool ShowAppPath,
        string? AppPath,
        string? Notice);

    public static string Render(IReadOnlyList<RegisterLocaltestClient.TestUser> users, Options options)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(Encode(options.Title)).Append("</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px}")
            .Append("label{display:block;margin:14px 0 4px;font-weight:600}select,input[type=text]{width:100%;padding:6px;box-sizing:border-box}")
            .Append("button{margin-top:18px;padding:8px 18px}.notice{color:#a00}</style></head><body>")
            .Append("<h1>").Append(Encode(options.Title)).Append("</h1><p>").Append(Encode(options.Intro)).Append("</p>");

        if (options.Notice is { Length: > 0 } notice)
        {
            html.Append("<p class=\"notice\">").Append(Encode(notice)).Append("</p>");
        }

        html.Append("<form method=\"post\" action=\"").Append(Encode(options.FormAction)).Append("\">");
        foreach (var (name, value) in options.Hidden)
        {
            html.Append("<input type=\"hidden\" name=\"").Append(Encode(name)).Append("\" value=\"").Append(Encode(value ?? string.Empty)).Append("\">");
        }

        html.Append("<label for=\"UserSelect\">Test user</label><select id=\"UserSelect\" name=\"UserSelect\">");
        foreach (var user in users)
        {
            var others = options.IncludeOtherParties ? user.OtherParties : [];
            if (others.Count > 0)
            {
                html.Append("<optgroup label=\"").Append(Encode(user.DisplayName)).Append("\">");
            }

            Option(html, $"{user.UserId}.{user.PartyId}", user.DisplayName);
            foreach (var party in others)
            {
                Option(html, $"{user.UserId}.{party.PartyId}", $"{user.DisplayName} for {party.DisplayName}");
            }

            if (others.Count > 0)
            {
                html.Append("</optgroup>");
            }
        }

        html.Append("</select>");

        html.Append("<label for=\"AuthenticationLevel\">Authentication level</label><select id=\"AuthenticationLevel\" name=\"AuthenticationLevel\">");
        foreach (var level in LocaltestAuthLevels.All)
        {
            html.Append("<option value=\"").Append(level.Level).Append('"').Append(level.Level == options.SelectedLevel ? " selected" : string.Empty)
                .Append('>').Append(Encode(level.Label)).Append("</option>");
        }

        html.Append("</select>");

        if (options.ShowAppPath)
        {
            html.Append("<label for=\"AppPathSelection\">App (org/app, optional)</label>")
                .Append("<input type=\"text\" id=\"AppPathSelection\" name=\"AppPathSelection\" placeholder=\"ttd/my-app\" value=\"").Append(Encode(options.AppPath ?? string.Empty)).Append("\">");
        }

        html.Append("<button type=\"submit\">Log in</button></form>");

        if (users.Count == 0)
        {
            html.Append("<p class=\"notice\">No test users: is the register component running?</p>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static void Option(StringBuilder html, string value, string text)
        => html.Append("<option value=\"").Append(Encode(value)).Append("\">").Append(Encode(text)).Append("</option>");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
