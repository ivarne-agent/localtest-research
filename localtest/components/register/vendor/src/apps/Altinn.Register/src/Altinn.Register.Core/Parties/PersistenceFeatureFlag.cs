using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Feature flags for persistence operations.
/// </summary>
public enum PersistenceFeatureFlag
    : uint
{
    /// <summary>
    /// Indicates that creating new party IDs is allowed.
    /// </summary>
    CreatePartyId = 1,
}

/// <summary>
/// Extensions for <see cref="PersistenceFeatureFlag"/>.
/// </summary>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "It does...")]
public static class PersistenceFeatureFlagExtensions
{
    extension(PersistenceFeatureFlag)
    {
        /// <summary>
        /// Checks if the configuration allows creating party IDs.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns><see langword="true"/> if creating party IDs is allowed; otherwise, <see langword="false"/>.</returns>
        public static bool CanCreatePartyId(IConfiguration configuration)
            => configuration.GetValue("Altinn:register:Party:CreatePartyId", defaultValue: false);

        /// <summary>
        /// Creates an array of enabled <see cref="PersistenceFeatureFlag"/>s based on the provided configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>An array of enabled <see cref="PersistenceFeatureFlag"/>s.</returns>
        public static PersistenceFeatureFlag[] FromConfiguration(IConfiguration configuration)
        {
            return CreateFlags([
                KeyValuePair.Create(PersistenceFeatureFlag.CreatePartyId, CanCreatePartyId(configuration)),
            ]);

            static PersistenceFeatureFlag[] CreateFlags(params ReadOnlySpan<KeyValuePair<PersistenceFeatureFlag, bool>> flags)
            {
                var enabledCount = 0;
                foreach (var kvp in flags)
                {
                    if (kvp.Value)
                    {
                        enabledCount++;
                    }
                }

                if (enabledCount == 0)
                {
                    return [];
                }

                var result = new PersistenceFeatureFlag[enabledCount];
                var index = 0;

                foreach (var kvp in flags)
                {
                    if (kvp.Value)
                    {
                        result[index++] = kvp.Key;
                    }
                }

                return result;
            }
        }
    }
}
