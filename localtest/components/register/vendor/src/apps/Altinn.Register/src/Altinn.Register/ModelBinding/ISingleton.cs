namespace Altinn.Register.ModelBinding;

/// <summary>
/// A contract for creating singleton instances in a type-safe manner. .
/// </summary>
internal interface ISingleton<TSelf>
    where TSelf : ISingleton<TSelf>
{
    /// <summary>
    /// Gets the singleton instance of the implementing type.
    /// </summary>
    public static abstract TSelf Instance { get; }
}
