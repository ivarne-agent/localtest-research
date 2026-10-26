using System.Globalization;
using System.Text;

namespace Localtest.Home.Instances;

/// <summary>
/// The page's URL state: the parameters of Storage's <c>GET storage/api/v1/instances</c>, by Storage's own names
/// (<c>InstanceQueryParameters</c> in <c>components/storage/vendor/src/Storage/Models/</c>), and the token to call it
/// with. The same query string drives the form (<c>/Home/Instances</c>), the list, the instance page and the downloads,
/// so a search can be linked to.
/// </summary>
public static class StorageQuery
{
    /// <summary>The instance owner identifier (<c>person:{ssn}</c>, <c>organisation:{orgNumber}</c>), which Storage takes as a header.</summary>
    public const string InstanceOwnerIdentifier = "instanceOwner.identifier";

    /// <summary>Storage's header for <see cref="InstanceOwnerIdentifier"/> (<c>[FromHeader]</c> in <c>InstanceQueryParameters</c>).</summary>
    public const string InstanceOwnerIdentifierHeader = "X-Ai-InstanceOwnerIdentifier";

    /// <summary>The query parameters that take one value, in the form's order. <c>order</c> is Storage's name for the sort (<c>SortBy</c>).</summary>
    public static readonly string[] Single =
    [
        "org", "appId", "process.currentTask", "process.isComplete", "process.endEvent", "instanceOwner.partyId",
        "confirmed", "excludeConfirmedBy", "status.isSoftDeleted", "status.isHardDeleted", "status.isArchived",
        "searchString", "mainVersionInclude", "mainVersionExclude", "A3Ref", "dataValues.A2ArchRef",
        "order", "size", "continuationToken", "includeDataElements",
    ];

    /// <summary>The date filters, each repeatable with an operator prefix (<c>gt:</c>, <c>gte:</c>, <c>lt:</c>, <c>lte:</c>, <c>eq:</c>).</summary>
    public static readonly string[] Dates = ["lastChanged", "created", "visibleAfter", "dueBefore", "process.ended"];

    /// <summary>Storage's query string for the request's Storage parameters (empty values dropped), with a leading <c>?</c> or empty.</summary>
    public static string ToStorage(IQueryCollection query)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var name in Single.Concat(Dates))
        {
            foreach (var value in query[name])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    pairs.Add(new(name, value.Trim()));
                }
            }
        }

        return QueryString(pairs);
    }

    /// <summary>The request's instance owner identifier, or null.</summary>
    public static string? Identifier(IQueryCollection query)
        => query[InstanceOwnerIdentifier].ToString() is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>A query string with a leading <c>?</c>, or empty for no pairs; each name and value escaped.</summary>
    public static string QueryString(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            builder.Append(builder.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}

/// <summary>Which token the page calls Storage with.</summary>
public enum TokenKind
{
    /// <summary>No <c>Authorization</c> header (Storage answers 401).</summary>
    None,

    /// <summary>A test user's, as <c>/Home/GetTestUserToken</c> gives it.</summary>
    User,

    /// <summary>A service owner's, as <c>/Home/GetTestOrgToken</c> gives it.</summary>
    Org,

    /// <summary>A token pasted into the form.</summary>
    Pasted,
}

/// <summary>
/// The token part of the URL state: <c>auth</c> (<c>none</c>, <c>user</c>, <c>org</c>, <c>token</c>) and the parameters of
/// the matching <c>/Home/GetTest…Token</c> endpoint (<c>userId</c>, <c>partyId</c>, <c>authenticationLevel</c>;
/// <c>orgNumber</c>, <c>scopes</c>) or the pasted <c>token</c>. Only the chosen kind's parameters are kept.
/// </summary>
public sealed record TokenChoice(TokenKind Kind, int? UserId = null, int? PartyId = null, int? AuthenticationLevel = null, string? OrgNumber = null, string? Scopes = null, string? Token = null)
{
    public const string KindParameter = "auth";

    /// <summary>The parameter names the token part uses; none is a Storage name.</summary>
    public static readonly string[] Parameters = [KindParameter, "userId", "partyId", "authenticationLevel", "orgNumber", "scopes", "token"];

    /// <summary>The token part of a request's query; an error for an unknown kind, a non-number, or a user without <c>userId</c>.</summary>
    public static (TokenChoice? Choice, string? Error) Parse(IQueryCollection query)
    {
        string? Value(string name) => query[name].ToString() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        int? Number(string name, ref string? error)
        {
            if (Value(name) is not { } text)
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            error ??= $"{name} must be a number.";
            return null;
        }

        string? error = null;
        switch (Value(KindParameter) ?? "none")
        {
            case "none":
                return (new TokenChoice(TokenKind.None), null);
            case "user":
                var userId = Number("userId", ref error);
                var partyId = Number("partyId", ref error);
                var level = Number("authenticationLevel", ref error);
                if (error is null && userId is null)
                {
                    error = "A test user's token needs userId.";
                }

                return error is null ? (new TokenChoice(TokenKind.User, userId, partyId, level), null) : (null, error);
            case "org":
                return (new TokenChoice(TokenKind.Org, OrgNumber: Value("orgNumber"), Scopes: Value("scopes")), null);
            case "token":
                return Value("token") is { } token
                    ? (new TokenChoice(TokenKind.Pasted, Token: token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..].Trim() : token), null)
                    : (null, "No token was pasted.");
            default:
                return (null, $"{KindParameter} must be none, user, org or token.");
        }
    }

    /// <summary>The token part as query pairs, for every link that carries the same token.</summary>
    public IEnumerable<KeyValuePair<string, string>> ToQuery()
    {
        switch (Kind)
        {
            case TokenKind.User:
                yield return new(KindParameter, "user");
                foreach (var (name, value) in new[] { ("userId", UserId), ("partyId", PartyId), ("authenticationLevel", AuthenticationLevel) })
                {
                    if (value is { } number)
                    {
                        yield return new(name, number.ToString(CultureInfo.InvariantCulture));
                    }
                }

                break;
            case TokenKind.Org:
                yield return new(KindParameter, "org");
                if (OrgNumber is not null)
                {
                    yield return new("orgNumber", OrgNumber);
                }

                if (Scopes is not null)
                {
                    yield return new("scopes", Scopes);
                }

                break;
            case TokenKind.Pasted:
                yield return new(KindParameter, "token");
                yield return new("token", Token!);
                break;
            default:
                yield return new(KindParameter, "none");
                break;
        }
    }

    /// <summary>A short description for the page: who Storage is asked as.</summary>
    public string Describe() => Kind switch
    {
        TokenKind.User => $"test user {UserId}" + (PartyId is { } party ? $" (party {party})" : string.Empty) + $", level {AuthenticationLevel ?? 2}",
        TokenKind.Org => $"service owner {OrgNumber ?? TestTokens.DefaultOrgNumber}" + (OrgNumber is null ? " (Digdir, ttd)" : string.Empty) + $", scopes {Scopes ?? TestTokens.DefaultServiceOwnerScopes}",
        TokenKind.Pasted => "the pasted token",
        _ => "no token",
    };
}
