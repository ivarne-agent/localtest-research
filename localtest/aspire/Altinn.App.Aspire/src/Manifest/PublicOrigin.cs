using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The manifest's <c>public</c> section: the public origin's default host and port, and the dedicated subdomains of the
/// host the front door serves on the same port, in file order.
/// </summary>
internal sealed record ManifestPublic
{
    /// <summary>The default public host.</summary>
    public string Host { get; set; } = "";

    /// <summary>The default public port.</summary>
    public int Port { get; set; }

    /// <summary>The dedicated subdomains (<c>pdf</c>, <c>af</c>, …).</summary>
    public IReadOnlyList<string> Subdomains { get; set; } = [];
}

/// <summary>
/// The public origin of one stack: its host, its port (null: allocated by Aspire when the AppHost runs) and the
/// manifest's subdomains. Resolves the manifest's placeholders as studioctl's reader does (<c>manifest/origin.go</c>):
/// <c>{{origin}}</c> = <c>http://host:port</c>, <c>{{host}}</c>, <c>{{port}}</c>, <c>{{origin:sub}}</c> =
/// <c>http://sub.host:port</c> and <c>{{host:sub}}</c> = <c>sub.host</c> for a listed subdomain; nothing else (the
/// database passwords' <c>{{password:role}}</c> are <see cref="LocaltestManifest.ResolvePasswords" />'s).
/// </summary>
internal sealed partial record PublicOrigin(string Host, int? Port, IReadOnlyList<string> Subdomains)
{
    /// <summary>The origin's scheme.</summary>
    public const string Scheme = "http";

    private const string OriginName = "origin";
    private const string HostName = "host";
    private const string PortName = "port";

    /// <summary>One <c>{{name}}</c> or <c>{{name:subdomain}}</c>.</summary>
    [GeneratedRegex(@"\{\{([^{}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <summary>One label of a host name: lower-case letters, digits and inner hyphens.</summary>
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    internal static partial Regex HostLabel();

    /// <summary>The origin's URL without a trailing slash, the port a hole while it is not allocated.</summary>
    public OriginValue Url => OriginValue.Url(Host, Port);

    /// <summary>A subdomain's host name, <c>sub.host</c>.</summary>
    public string SubdomainHost(string subdomain) => subdomain + "." + Host;

    /// <summary>A subdomain's URL, <c>http://sub.host:port</c>.</summary>
    public OriginValue SubdomainUrl(string subdomain) => OriginValue.Url(SubdomainHost(subdomain), Port);

    /// <summary>The host names that reach the origin: the host, then each subdomain's in file order.</summary>
    public IReadOnlyList<string> Hosts => [Host, .. Subdomains.Select(SubdomainHost)];

    /// <summary>The value with its placeholders resolved; the manifest reader has checked them.</summary>
    public OriginValue Resolve(string value)
    {
        var parts = new List<OriginValue>();
        var at = 0;
        foreach (Match match in Placeholder().Matches(value))
        {
            parts.Add(OriginValue.Literal(value[at..match.Index]));
            parts.Add(ResolveOne(match.Groups[1].Value) ?? OriginValue.Literal(match.Value));
            at = match.Index + match.Length;
        }
        parts.Add(OriginValue.Literal(value[at..]));
        return OriginValue.Concat(parts);
    }

    /// <summary>The fault of the value's first bad placeholder, or null when every one is known.</summary>
    public string? PlaceholderFault(string value)
    {
        var matches = Placeholder().Matches(value);
        var opened = 0;
        for (
            var i = value.IndexOf("{{", StringComparison.Ordinal);
            i >= 0;
            i = value.IndexOf("{{", i + 2, StringComparison.Ordinal)
        )
        {
            opened++;
        }
        if (opened != matches.Count)
        {
            return $"bad placeholder in \"{value}\": an unclosed {{{{";
        }
        foreach (Match match in matches)
        {
            if (LocaltestManifest.PasswordRole(match.Groups[1].Value) is not null)
            {
                continue; // LocaltestManifest.CheckPasswords
            }
            if (ResolveOne(match.Groups[1].Value) is null)
            {
                var (name, sub) = Split(match.Groups[1].Value);
                return
                    sub is not null
                    && name is OriginName or HostName
                    && !Subdomains.Contains(sub, StringComparer.Ordinal)
                    ? $"bad placeholder {match.Value}: \"{sub}\" is not a listed subdomain ({string.Join(", ", Subdomains)})"
                    : $"bad placeholder {match.Value}: one of {{{{origin}}}}, {{{{host}}}}, {{{{port}}}}, {{{{origin:<sub>}}}}, {{{{host:<sub>}}}}, {{{{password:<role>}}}}";
            }
        }
        return null;
    }

    private OriginValue? ResolveOne(string inner)
    {
        var (name, sub) = Split(inner);
        if (sub is not null)
        {
            if (!Subdomains.Contains(sub, StringComparer.Ordinal))
            {
                return null;
            }
            return name switch
            {
                OriginName => SubdomainUrl(sub),
                HostName => OriginValue.Literal(SubdomainHost(sub)),
                _ => null,
            };
        }
        return name switch
        {
            OriginName => Url,
            HostName => OriginValue.Literal(Host),
            PortName => OriginValue.PortOf(Port),
            _ => null,
        };
    }

    private static (string Name, string? Sub) Split(string inner) =>
        inner.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0
            ? (inner[..colon], inner[(colon + 1)..])
            : (inner, null);

    /// <summary>The fault of a host name that cannot be the public origin's, or null.</summary>
    public static string? HostFault(string host)
    {
        const int maxHost = 253;
        if (host.Length is 0 or > maxHost)
        {
            return $"bad host name \"{host}\"";
        }
        var labels = host.Split('.');
        if (labels.Any(l => !HostLabel().IsMatch(l)))
        {
            return $"bad host name \"{host}\": lower-case letters, digits and hyphens in dot-separated labels";
        }
        return int.TryParse(labels[^1], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? $"bad host name \"{host}\": a name, not an IP address"
            : null;
    }
}

/// <summary>
/// A value derived from the public origin: literal text and, while the port is allocated by Aspire, holes for the
/// port. A literal value is a string; one with a hole becomes a <see cref="ReferenceExpression" /> over the front
/// door's endpoint, which Aspire resolves once it has allocated the port, before a container or an app starts.
/// </summary>
internal sealed class OriginValue
{
    /// <summary>The parts: literal text, or null for the port.</summary>
    private readonly IReadOnlyList<string?> _parts;

    private OriginValue(IReadOnlyList<string?> parts) => _parts = parts;

    /// <summary>Whether the value has no port hole.</summary>
    public bool IsLiteral => _parts.All(p => p is not null);

    /// <summary>A literal value.</summary>
    public static OriginValue Literal(string value) => new([value]);

    /// <summary>The port: literal when it is known, a hole while Aspire allocates it.</summary>
    public static OriginValue PortOf(int? port) =>
        port is { } known ? Literal(known.ToString(CultureInfo.InvariantCulture)) : new([null]);

    /// <summary><c>http://host:port</c>, the port always written, as studioctl writes its public URLs.</summary>
    public static OriginValue Url(string host, int? port) =>
        Concat([Literal($"{PublicOrigin.Scheme}://{host}:"), PortOf(port)]);

    /// <summary>The values one after the other.</summary>
    public static OriginValue Concat(IEnumerable<OriginValue> values)
    {
        var parts = new List<string?>();
        foreach (var part in values.SelectMany(v => v._parts))
        {
            if (part is not null && parts.Count > 0 && parts[^1] is { } previous)
            {
                parts[^1] = previous + part;
            }
            else if (part is not { Length: 0 })
            {
                parts.Add(part);
            }
        }
        return new(parts.Count == 0 ? [""] : parts);
    }

    /// <summary>The value followed by literal text.</summary>
    public OriginValue Then(string literal) => Concat([this, Literal(literal)]);

    /// <summary>The value with <paramref name="port" /> in each hole.</summary>
    public string Render(int port) =>
        string.Concat(_parts.Select(p => p ?? port.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The literal value; throws for a value with a hole.</summary>
    public string Text =>
        IsLiteral
            ? string.Concat(_parts)
            : throw new InvalidOperationException("The public port is not allocated yet.");

    /// <summary>
    /// The value for an environment variable: the string, or an expression whose holes are <paramref name="port" />,
    /// the front door's endpoint's port as the host sees it.
    /// </summary>
    public object ToEnvironmentValue(EndpointReferenceExpression port)
    {
        if (IsLiteral)
        {
            return Text;
        }
        var builder = new ReferenceExpressionBuilder();
        foreach (var part in _parts)
        {
            if (part is null)
            {
                builder.AppendFormatted(port);
            }
            else
            {
                // The builder's literals are the expression's format string, where a brace is doubled.
                builder.AppendLiteral(
                    part.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)
                );
            }
        }
        return builder.Build();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var part in _parts)
        {
            text.Append(part ?? "{port}");
        }
        return text.ToString();
    }
}
