using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Import job queue status, consisting of the highest enqueued item and the highest known source item at the time of
/// last queue update.
/// </summary>
/// <remarks>
/// It's important that <c>default(ImportJobQueueStatus)</c> results in a value where both <see cref="EnqueuedMax"/> and
/// <see cref="SourceMax"/> are <c>0</c>, as this is used to indicate that the job has not yet been created.
/// </remarks>
public readonly record struct ImportJobQueueStatus
{
    private readonly long _sourceMax;

    /// <summary>
    /// Gets the highest enqueued item.
    /// </summary>
    public readonly required ulong EnqueuedMax { get; init; }

    /// <summary>
    /// Gets the highest known item at the source.
    /// </summary>
    public readonly required ulong? SourceMax
    {
        get => _sourceMax switch
        {
            -1 => null,
            >= 0 => (ulong)_sourceMax,
            _ => Unreachable<ulong?>(),
        };

        init => _sourceMax = value switch
        {
            null => -1,
            ulong u => checked((long)u),
        };
    }

    [DoesNotReturn]
    [ExcludeFromCodeCoverage]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T Unreachable<T>() => throw new UnreachableException();
}
