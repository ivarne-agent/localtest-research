using System.Runtime.ExceptionServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Persistence.AsyncEnumerables;

/// <summary>
/// A <see cref="IAsyncEnumerable{T}"/> that throws when enumerated.
/// </summary>
/// <typeparam name="T">The item type - unused.</typeparam>
internal class ThrowingAsyncEnumerable<T>
    : IAsyncEnumerable<T>
    , IAsyncResourceOwner
{
    private readonly AsyncDisposableCollection _resources = new();
    private readonly ExceptionDispatchInfo _exception;

    /// <summary>
    /// Initializes a new <see cref="ThrowingAsyncEnumerable{T}"/>.
    /// </summary>
    /// <param name="exception">The exception to throw.</param>
    public ThrowingAsyncEnumerable(Exception exception)
    {
        Guard.IsNotNull(exception);

        _exception = ExceptionDispatchInfo.Capture(exception);
    }

    /// <inheritdoc/>
    public void Adopt(ReadOnlySpan<IAsyncDisposable> resources)
        => _resources.Adopt(resources);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
        => _resources.DisposeAsync();

    /// <inheritdoc/>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await using var resources = _resources;
        _exception.Throw();
        yield break; // unreachable - just here for the code to compile
    }
}
