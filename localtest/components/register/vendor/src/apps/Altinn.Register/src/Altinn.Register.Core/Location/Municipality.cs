using System.Diagnostics;

namespace Altinn.Register.Core.Location;

/// <summary>
/// Represents a municipality with its number, name, and status.
/// </summary>
[DebuggerDisplay("{Number,nq} {Name,nq} ({Status,nq})")]
public sealed record Municipality
{
    /// <summary>
    /// Gets the municipality number.
    /// </summary>
    public required MunicipalityNumber Number { get; init; }

    /// <summary>
    /// Gets the municipality name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the municipality status.
    /// </summary>
    public required MunicipalityStatus Status { get; init; }
}
