namespace Altinn.App.Aspire.Manifest;

/// <summary>One container of the stack, every placeholder resolved: what the package turns into an Aspire resource.</summary>
internal sealed record ResolvedComponent
{
    /// <summary>The manifest name.</summary>
    public required string Name { get; init; }

    /// <summary>The optional group; null for the always-on components.</summary>
    public string? Group { get; init; }

    /// <summary>The image.</summary>
    public required ImageReference Image { get; init; }

    /// <summary>The port in the container.</summary>
    public required int ContainerPort { get; init; }

    /// <summary>The fixed host port; null when the component publishes none.</summary>
    public int? HostPort { get; init; }

    /// <summary>The HTTP health path; null when it serves no HTTP.</summary>
    public string? HealthPath { get; init; }

    /// <summary>The environment, sorted by name.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>The image's command replaced (container arguments); null to keep the image's.</summary>
    public IReadOnlyList<string>? Command { get; init; }

    /// <summary>The container runtime's health check test (not run under Aspire; kept for the comparison).</summary>
    public IReadOnlyList<string>? ContainerHealthCheck { get; init; }

    /// <summary>The mounts in manifest order.</summary>
    public required IReadOnlyList<ResolvedMount> Mounts { get; init; }

    /// <summary>The network aliases from the manifest.</summary>
    public required IReadOnlyList<string> NetworkAliases { get; init; }

    /// <summary>The <c>--add-host</c> entries, <c>name:host-gateway</c>.</summary>
    public required IReadOnlyList<string> ExtraHosts { get; init; }

    /// <summary>The components that must be healthy first.</summary>
    public required IReadOnlyList<string> DependsOn { get; init; }

    /// <summary>The front-door routes to the component.</summary>
    public required IReadOnlyList<ResolvedRoute> Routes { get; init; }

    /// <summary>Whether the container runs as the developer's user (unused by every component today).</summary>
    public bool HostUser { get; init; }
}

/// <summary>A bind mount (host path) or a named volume.</summary>
/// <param name="IsVolume">True for a named volume, false for a bind mount.</param>
/// <param name="Source">The volume's name or the host path.</param>
/// <param name="Target">The path in the container.</param>
/// <param name="ReadOnly">Whether the mount is read-only.</param>
internal sealed record ResolvedMount(bool IsVolume, string Source, string Target, bool ReadOnly);

/// <summary>A front-door route.</summary>
/// <param name="Component">The component the route forwards to.</param>
/// <param name="Host">The public host.</param>
/// <param name="Prefix">The path prefix; empty for the whole host.</param>
/// <param name="Rewrite">The path forwarded in place of the prefix; null to keep the path.</param>
internal sealed record ResolvedRoute(string Component, string Host, string Prefix, string? Rewrite);
