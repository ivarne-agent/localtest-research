namespace Altinn.Register.Contracts.Testing;

/// <summary>
/// Extension methods for <see cref="Party"/> and subclasses.
/// </summary>
public static class PartyExtensions
{
    /// <summary>
    /// Ensures a party uuid is valid.
    /// </summary>
    /// <param name="uuid">The uuid.</param>
    internal static void EnsureUuid(ref Guid uuid)
    {
        if (uuid == Guid.Empty)
        {
            // Note: we don't use v7 here, as they are harder to visually distinguish from each other when debugging.
            uuid = Guid.NewGuid();
        }
    }
}
