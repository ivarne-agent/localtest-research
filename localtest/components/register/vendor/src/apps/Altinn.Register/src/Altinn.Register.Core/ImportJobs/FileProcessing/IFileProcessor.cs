namespace Altinn.Register.Core.ImportJobs.FileProcessing;

/// <summary>
/// Interface for processing files represented by <see cref="OpenedFileInfo"/> or derived types.
/// </summary>
/// <typeparam name="T">The type of <see cref="OpenedFileInfo"/> or derived type.</typeparam>
public interface IFileProcessor<T>
    where T : OpenedFileInfo
{
    /// <summary>
    /// Processes the file represented by <paramref name="fileInfo"/>.
    /// </summary>
    /// <param name="fileInfo">The file information.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task ProcessFileAsync(T fileInfo, CancellationToken cancellationToken);
}
