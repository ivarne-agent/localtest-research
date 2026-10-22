namespace Altinn.App.Aspire.Manifest;

/// <summary>A container image reference split the way Aspire takes it: registry, image, tag and digest.</summary>
/// <param name="Registry">The registry host, or null for the runtime's default.</param>
/// <param name="Image">The repository path without the registry.</param>
/// <param name="Tag">The tag, or null.</param>
/// <param name="Digest">The digest (<c>sha256:...</c>), or null.</param>
/// <param name="Floating">Whether the tag moves, so the image is pulled on every start.</param>
internal sealed record ImageReference(string? Registry, string Image, string? Tag, string? Digest, bool Floating)
{
    /// <summary>The full reference as the manifest writes it: <c>registry/image:tag[@digest]</c>.</summary>
    public string Reference =>
        (Registry is null ? Image : Registry + "/" + Image)
        + ":"
        + (Tag ?? "latest")
        + (Digest is null ? "" : "@" + Digest);

    /// <summary>The digest's hex part, as <c>WithImageSHA256</c> takes it.</summary>
    public string? Sha256 =>
        Digest is not null && Digest.StartsWith("sha256:", StringComparison.Ordinal)
            ? Digest["sha256:".Length..]
            : null;

    /// <summary>
    /// Splits a repository (<c>ghcr.io/org/name</c>) and a tag that may carry a digest (<c>7.2-alpine@sha256:...</c>).
    /// The first path segment is the registry when it looks like a host (has a dot or a port, or is localhost).
    /// </summary>
    public static ImageReference From(string repository, string? tag, bool floating)
    {
        string? digest = null;
        if (tag is not null && tag.IndexOf('@', StringComparison.Ordinal) is var at and >= 0)
        {
            digest = tag[(at + 1)..];
            tag = at == 0 ? null : tag[..at];
        }
        string? registry = null;
        var image = repository;
        var slash = repository.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            var first = repository[..slash];
            if (
                first.Contains('.', StringComparison.Ordinal)
                || first.Contains(':', StringComparison.Ordinal)
                || string.Equals(first, "localhost", StringComparison.Ordinal)
            )
            {
                registry = first;
                image = repository[(slash + 1)..];
            }
        }
        return new ImageReference(registry, image, string.IsNullOrEmpty(tag) ? null : tag, digest, floating);
    }
}
