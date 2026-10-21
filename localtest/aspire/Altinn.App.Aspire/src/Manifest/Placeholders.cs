using System.Globalization;
using System.Text;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// Resolves the manifest's placeholders as the Go loader does (<c>internal/localtestv2/manifest/placeholders.go</c>):
/// <c>{{name}}</c> or <c>{{name:argument}}</c>; single braces (<c>{0}</c>, <c>{Org}</c>) belong to the components and
/// pass through.
/// </summary>
internal sealed class Placeholders(LocaltestManifest manifest, LocaltestTopology topology)
{
    private const string Open = "{{";
    private const string Close = "}}";

    /// <summary>Resolves every placeholder in <paramref name="value" />; an unknown one throws.</summary>
    public string Expand(string value)
    {
        var output = new StringBuilder();
        var rest = value.AsSpan();
        while (true)
        {
            var start = rest.IndexOf(Open, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Append(rest);
                return output.ToString();
            }
            output.Append(rest[..start]);
            rest = rest[(start + Open.Length)..];
            var end = rest.IndexOf(Close, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidDataException($"Unterminated placeholder in \"{value}\".");
            }
            output.Append(Resolve(rest[..end].ToString(), value));
            rest = rest[(end + Close.Length)..];
        }
    }

    /// <summary>Resolves every string of a list.</summary>
    public IReadOnlyList<string> ExpandAll(IEnumerable<string> values) => [.. values.Select(Expand)];

    private string Resolve(string text, string context)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? text : text[..colon];
        var argument = colon < 0 ? null : text[(colon + 1)..];
        return (name, argument) switch
        {
            ("platform", null) => Topology(topology.Url, LocaltestTopology.App, text, context) + "/",
            ("url", { Length: > 0 } id) => Topology(topology.Url, id, text, context),
            ("host", { Length: > 0 } id) => Topology(topology.Host, id, text, context),
            ("certPath", null) => manifest.Certificate.ContainerPath,
            ("postgresUser", null) => manifest.Postgres.User,
            ("postgresPassword", null) => manifest.Postgres.Password,
            ("alias", { Length: > 0 } component) => Alias(component, text, context),
            ("port", { Length: > 0 } component) => Component(component, text, context)
                .ContainerPort.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"Unknown placeholder {{{{{text}}}}} in \"{context}\"."),
        };
    }

    private static string Topology(Func<string, string?> lookup, string id, string text, string context) =>
        lookup(id)
        ?? throw new InvalidDataException($"{{{{{text}}}}} in \"{context}\" names no topology component {id}.");

    private ManifestComponent Component(string name, string text, string context) =>
        manifest.Component(name)
        ?? throw new InvalidDataException($"{{{{{text}}}}} in \"{context}\" names no component {name}.");

    /// <summary>
    /// A component's first network alias. In Aspire every container is also reachable by its resource name, which the
    /// package keeps equal to the component name, so a component without aliases would resolve to its name; the
    /// manifest only uses <c>{{alias:...}}</c> for components that have one (Postgres, Redis), as the Go loader requires.
    /// </summary>
    private string Alias(string name, string text, string context)
    {
        var component = Component(name, text, context);
        return component.NetworkAliases.Count > 0
            ? component.NetworkAliases[0]
            : throw new InvalidDataException($"{{{{{text}}}}} in \"{context}\": {name} has no network alias.");
    }
}
