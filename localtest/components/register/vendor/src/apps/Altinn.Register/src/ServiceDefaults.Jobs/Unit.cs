namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Represents a unit type. Similar to <see cref="void"/>, but can be used as a type argument to
/// indicate that a job does not produce a meaningful value.
/// </summary>
public readonly record struct Unit
{
    /// <summary>
    /// The singleton value of the <see cref="Unit"/> type.
    /// </summary>
    public static readonly Unit Value = default;
}
