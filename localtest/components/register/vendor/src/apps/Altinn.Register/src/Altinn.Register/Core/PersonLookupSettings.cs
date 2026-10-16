namespace Altinn.Register.Core
{
    /// <summary>
    /// Represents settings related to the person lookup endpoint.
    /// </summary>
    public class PersonLookupSettings
    {
        /// <summary>
        /// The number of seconds a successfully retrieved person object should be cached.
        /// </summary>
        public int PersonCacheLifetimeSeconds { get; set; } = 3600;
    }
}
