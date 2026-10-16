using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Persistence.AsyncEnumerables;

/// <summary>
/// A <see cref="IAsyncEnumerable{T}"/> that is also responsible for cleaning
/// up some resources.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal class ResourceOwningEnumerable<T>
    : IAsyncEnumerable<T>
    , IAsyncResourceOwner
{
    private readonly AsyncDisposableCollection _resources = new();
    private readonly IAsyncEnumerable<T> _source;

    /// <summary>
    /// Initializes a new <see cref="ResourceOwningEnumerable{T}"/>.
    /// </summary>
    /// <param name="source">The inner <see cref="IAsyncEnumerable{T}"/>.</param>
    public ResourceOwningEnumerable(IAsyncEnumerable<T> source)
    {
        Guard.IsNotNull(source);

        _source = source;
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

        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
