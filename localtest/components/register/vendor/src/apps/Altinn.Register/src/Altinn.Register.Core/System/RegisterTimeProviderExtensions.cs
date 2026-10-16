namespace System;

/// <summary>
/// Extensions for <see cref="TimeProvider"/>.
/// </summary>
public static class RegisterTimeProviderExtensions
{
    /// <param name="timeProvider">The <see cref="TimeProvider"/>.</param>
    extension(TimeProvider timeProvider)
    {
        /// <summary>
        /// Generates a version 7 universally unique identifier (UUID) based on the current UTC time.
        /// </summary>
        /// <returns>A GUID representing the generated version 7 UUID, suitable for use as a unique identifier in distributed
        /// systems.</returns>
        public Guid GetUuidV7()
            => Guid.CreateVersion7(timeProvider.GetUtcNow());
    }
}
