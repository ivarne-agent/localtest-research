using System.Globalization;
using System.Text.Json;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// studioctl's golden snapshot (<c>internal/cmd/env/localtestv2/testdata/environment.golden.json</c>), read into the
/// fields the package resolves too.
/// </summary>
internal sealed class Golden
{
    /// <summary>The certificate directory the golden file was written with.</summary>
    public const string CertificateDirectory = "/home/dev/.altinn-studio/certs";

    private const string ContainerIdPrefix = "container:localtest-v2-";

    private Golden(JsonElement root) => Root = root;

    public JsonElement Root { get; }

    public static Golden Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "environment.golden.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return new Golden(document.RootElement.Clone());
    }

    public IReadOnlyList<GoldenContainer> Containers(string selection)
    {
        var resources = Root.GetProperty(selection).GetProperty("resources").EnumerateArray().ToList();
        var images = resources
            .Where(r => r.GetProperty("id").GetString()!.StartsWith("image:", StringComparison.Ordinal))
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r);
        return
        [
            .. resources
                .Where(r => r.GetProperty("id").GetString()!.StartsWith(ContainerIdPrefix, StringComparison.Ordinal))
                .Select(r => GoldenContainer.From(r, images)),
        ];
    }

    public IReadOnlyList<string> HealthTargets(string selection) =>
        [.. Root.GetProperty(selection).GetProperty("healthTargets").EnumerateArray().Select(t => t.GetString()!)];

    public IReadOnlyList<GoldenRoute> Routes(string selection) =>
        [
            .. Root.GetProperty(selection)
                .GetProperty("routeTable")
                .GetProperty("routes")
                .EnumerateArray()
                .Select(GoldenRoute.From),
        ];

    public static string NameOf(string containerId) => containerId[ContainerIdPrefix.Length..];
}

internal sealed record GoldenContainer(
    string Name,
    string? PulledImage,
    bool BuiltImage,
    IReadOnlyList<(string Host, string Container)> Ports,
    IReadOnlyList<(string Type, string Source, string Target, bool ReadOnly)> Volumes,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string>? Command,
    IReadOnlyList<string> ExtraHosts,
    IReadOnlyList<string> NetworkAliases,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string>? HealthCheckTest,
    string User,
    string UsernsMode
)
{
    public static GoldenContainer From(JsonElement r, IReadOnlyDictionary<string, JsonElement> images)
    {
        var image = images[r.GetProperty("image").GetString()!];
        return new GoldenContainer(
            Golden.NameOf(r.GetProperty("id").GetString()!),
            image.TryGetProperty("ref", out var reference) ? reference.GetString() : null,
            image.TryGetProperty("contextPath", out _),
            [.. Array(r, "ports").Select(p => (Text(p, "HostPort"), Text(p, "ContainerPort")))],
            [
                .. Array(r, "volumes")
                    .Select(v =>
                        (
                            Text(v, "Type"),
                            Text(v, "HostPath"),
                            Text(v, "ContainerPath"),
                            v.GetProperty("ReadOnly").GetBoolean()
                        )
                    ),
            ],
            Array(r, "env")
                .Select(e => e.GetString()!.Split('=', 2))
                .ToDictionary(kv => kv[0], kv => kv[1], StringComparer.Ordinal),
            r.TryGetProperty("command", out var command)
                ? [.. command.EnumerateArray().Select(c => c.GetString()!)]
                : null,
            Strings(r, "extraHosts"),
            Strings(r, "networkAliases"),
            [.. Strings(r, "dependsOn").Select(Golden.NameOf)],
            r.TryGetProperty("healthCheck", out var check)
                ? [.. check.GetProperty("Test").EnumerateArray().Select(t => t.GetString()!)]
                : null,
            r.TryGetProperty("user", out var user) ? user.GetString()! : "",
            r.TryGetProperty("usernsMode", out var userns) ? userns.GetString()! : ""
        );
    }

    private static IEnumerable<JsonElement> Array(JsonElement r, string name) =>
        r.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static IReadOnlyList<string> Strings(JsonElement r, string name) =>
        [.. Array(r, name).Select(s => s.GetString()!)];

    private static string Text(JsonElement e, string name) =>
        e.GetProperty(name) is var value && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32().ToString(CultureInfo.InvariantCulture)
            : value.GetString() ?? "";
}

internal sealed record GoldenRoute(string Component, string Host, string Prefix, string? Rewrite, bool Enabled)
{
    public static GoldenRoute From(JsonElement r)
    {
        var match = r.GetProperty("match");
        string? rewrite = null;
        if (r.TryGetProperty("metadata", out var metadata))
        {
            rewrite = metadata
                .EnumerateArray()
                .Where(m => m.GetProperty("key").GetString() == "rewritePrefix")
                .Select(m => m.GetProperty("value").GetString())
                .FirstOrDefault();
        }
        return new GoldenRoute(
            r.GetProperty("component").GetString()!,
            match.GetProperty("host").GetString()!,
            match.TryGetProperty("pathPrefix", out var prefix) ? prefix.GetString()! : "",
            rewrite,
            r.GetProperty("enabled").GetBoolean()
        );
    }
}
