using System.Text.Json;

namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// State-data for a saga.
/// </summary>
/// <typeparam name="TSelf">The type of the state.</typeparam>
public interface ISagaStateData<TSelf>
    where TSelf : ISagaStateData<TSelf>
{
    /// <summary>
    /// The state type, stored in the database so that we can identify the type of the state object.
    /// </summary>
    public static abstract string StateType { get; }

    /// <summary>
    /// Read and convert the JSON to <typeparamref name="TSelf"/>.
    /// </summary>
    /// <param name="stream">The json <see cref="Stream"/>.</param>
    /// <param name="stateType">The state-type stored in the database.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> being used.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The value that was converted.</returns>
    public static virtual ValueTask<TSelf?> ReadAsync(
        Stream stream,
        string stateType,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (stateType == TSelf.StateType)
        {
            return JsonSerializer.DeserializeAsync<TSelf>(stream, options, cancellationToken);
        }

        return default;
    }
}
