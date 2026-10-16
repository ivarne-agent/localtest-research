namespace Altinn.Register.Persistence.AsyncEnumerables;

/// <summary>
/// A manager of async resource lifetimes.
/// </summary>
internal interface IAsyncResourceOwner
    : IAsyncDisposable
{
    /// <summary>
    /// Adopts ownership of a set of <see cref="IAsyncDisposable"/>, such that when this
    /// resource owner is disposed, the <paramref name="resources"/> is also disposed.
    /// </summary>
    /// <param name="resources">The resources to adopt.</param>
    public void Adopt(ReadOnlySpan<IAsyncDisposable> resources);

    /// <summary>
    /// Adopts ownership of a <see cref="IAsyncDisposable"/>, such that when this
    /// resource owner is disposed, the <paramref name="resource"/> is also disposed.
    /// </summary>
    /// <param name="resource">The resource to adopt.</param>
    public void Adopt(IAsyncDisposable resource)
        => Adopt([resource]);
}
