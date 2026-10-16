using CommunityToolkit.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Utils;

/// <summary>
/// Extensions for <see cref="Random"/>.
/// </summary>
internal static class RandomExtensions
{
    extension(Random random)
    {
        public double Next(double minValue, double maxValue)
        {
            if (minValue > maxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than or equal to minValue.");
            }

            return ((maxValue - minValue) * random.NextDouble()) + minValue;
        }
    }
}
