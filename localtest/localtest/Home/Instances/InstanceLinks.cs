using System.Text.Json;
using Localtest.Proxy;

namespace Localtest.Home.Instances;

/// <summary>
/// The links out of the instances page, each shown only while what it points at runs: the inbox (Arbeidsflate, on its
/// subdomain, with the party preselected), the instance's dialog there (Arbeidsflate and the Dialogporten adapter), and
/// the running instance in its app or its receipt. A service runs when the runner gave the front door its address
/// (<c>services__{name}__http__0</c>), as for the route table; an app when it is registered with the front door.
/// </summary>
public sealed class InstanceLinks(IConfiguration configuration, LocaltestSettings localtest, AppRoutes apps)
{
    /// <summary>Whether the inbox runs: the <c>arbeidsflate</c> service has an address and its subdomain is on.</summary>
    public bool Arbeidsflate => Runs("arbeidsflate") && localtest.HostOf(LocaltestSettings.ArbeidsflateSubdomain) is not null;

    /// <summary>Whether the instances have dialogs: the inbox runs and so does the Dialogporten adapter, which creates them.</summary>
    public bool Dialogs => Arbeidsflate && Runs("dialogporten-adapter");

    /// <summary>Whether the platform receipt runs (the <c>receipt</c> group).</summary>
    public bool Receipt => Runs("receipt");

    /// <summary>The inbox with the party preselected (<c>?party=</c> takes the party's URN); without a URN, the inbox.</summary>
    public string Inbox(string? partyUrn)
        => localtest.OriginOf(LocaltestSettings.ArbeidsflateSubdomain) + "/" + (partyUrn is null ? string.Empty : "?party=" + Uri.EscapeDataString(partyUrn));

    /// <summary>The dialog's page in the inbox, as the app runners' <c>ArbeidsflateDialogUrl</c> has it.</summary>
    public string Dialog(Guid dialogId) => localtest.OriginOf(LocaltestSettings.ArbeidsflateSubdomain) + "/inbox/" + dialogId;

    /// <summary>
    /// The instance in its app (one that is not complete, or a completed one while the receipt does not run: the app shows
    /// its own receipt), or the platform receipt for a completed one; null when the app is not registered with the front
    /// door and the receipt is not the link.
    /// </summary>
    public (string Label, string Url)? Running(string appId, int partyId, Guid instanceGuid, bool complete)
    {
        if (complete && Receipt)
        {
            return ("Receipt", $"{localtest.PlatformEndpoint}receipt/{partyId}/{instanceGuid}");
        }

        var parts = appId.Split('/');
        if (parts.Length != 2 || apps.Get(parts[0], parts[1]) is null)
        {
            return null;
        }

        return (complete ? "Receipt in the app" : "Open in the app", $"{localtest.PlatformEndpoint}{parts[0]}/{parts[1]}/#/instance/{partyId}/{instanceGuid}");
    }

    private bool Runs(string service) => !string.IsNullOrEmpty(configuration[$"services:{service}:http:0"]);

    /// <summary>
    /// The instance's dialog id: the adapter's record of it in the data values (<c>dialog.id</c>, also where an app
    /// supplies its own), else derived as the adapter derives it, the instance GUID made a UUID v7 on the instance's
    /// creation time (<c>ISyncInstanceToDialogService</c>: <c>dto.InstanceId.ToVersion7(dto.InstanceCreatedAt)</c>).
    /// </summary>
    public static Guid? DialogId(JsonElement instance, Guid instanceGuid)
    {
        if (instance.TryGetProperty("dataValues", out var values) && values.ValueKind == JsonValueKind.Object
            && values.TryGetProperty("dialog.id", out var recorded) && recorded.ValueKind == JsonValueKind.String
            && Guid.TryParse(recorded.GetString(), out var dialogId))
        {
            return dialogId;
        }

        return instance.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(created.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
                ? ToVersion7(instanceGuid, at)
                : null;
    }

    /// <summary>
    /// A copy of the Dialogporten adapter's <c>GuidExtensions.ToVersion7</c>
    /// (<c>components/dialogporten-adapter/vendor/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/GuidExtensions.cs</c>):
    /// the GUID's bytes (big-endian) with the first 48 bits replaced by the Unix time in milliseconds, version 7 and the
    /// RFC 4122 variant.
    /// </summary>
    public static Guid ToVersion7(Guid guid, DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        var millis = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)((millis >> 40) & 0xFF);
        bytes[1] = (byte)((millis >> 32) & 0xFF);
        bytes[2] = (byte)((millis >> 24) & 0xFF);
        bytes[3] = (byte)((millis >> 16) & 0xFF);
        bytes[4] = (byte)((millis >> 8) & 0xFF);
        bytes[5] = (byte)(millis & 0xFF);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>
/// The parties' URNs for the inbox's <c>?party=</c>, looked up in the Register's localtest API by service name
/// (<c>register/api/localtest/test-users</c> for persons, <c>organizations</c> for organisations): a person is
/// <c>urn:altinn:person:identifier-no:{ssn}</c>, an organisation <c>urn:altinn:organization:identifier-no:{orgNumber}</c>,
/// as Dialogporten names parties. A self-identified user has no such URN. Read per page, only while the inbox runs.
/// </summary>
public sealed class PartyUrns(IHttpClientFactory httpClientFactory)
{
    public async Task<IReadOnlyDictionary<int, string>> Lookup(CancellationToken cancellationToken)
    {
        var urns = new Dictionary<int, string>();
        using var client = httpClientFactory.CreateClient();
        try
        {
            using (var users = JsonDocument.Parse(await client.GetStringAsync("http://register/register/api/localtest/test-users", cancellationToken)))
            {
                foreach (var user in users.RootElement.EnumerateArray())
                {
                    Add(urns, user, "person");
                    if (user.TryGetProperty("actingFor", out var actingFor) && actingFor.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var party in actingFor.EnumerateArray())
                        {
                            Add(urns, party, party.TryGetProperty("partyType", out var type) ? type.GetString() : null);
                        }
                    }
                }
            }

            using var organizations = JsonDocument.Parse(await client.GetStringAsync("http://register/register/api/localtest/organizations", cancellationToken));
            foreach (var organization in organizations.RootElement.EnumerateArray())
            {
                Add(urns, organization, "organization");
            }
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex) || ex is JsonException or InvalidOperationException)
        {
            // Without the Register the inbox links open the inbox without a party.
        }

        return urns;
    }

    private static void Add(Dictionary<int, string> urns, JsonElement party, string? partyType)
    {
        if (!party.TryGetProperty("partyId", out var id) || !id.TryGetInt32(out var partyId) || urns.ContainsKey(partyId))
        {
            return;
        }

        var urn = partyType switch
        {
            "person" when Text(party, "ssn") is { } ssn => "urn:altinn:person:identifier-no:" + ssn,
            "organization" when Text(party, "orgNumber") is { } orgNumber => "urn:altinn:organization:identifier-no:" + orgNumber,
            _ => null,
        };
        if (urn is not null)
        {
            urns[partyId] = urn;
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
