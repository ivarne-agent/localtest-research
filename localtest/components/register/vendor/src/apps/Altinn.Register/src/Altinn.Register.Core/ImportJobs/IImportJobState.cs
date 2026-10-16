using System.Text.Json;

namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// State for an import job.
/// </summary>
/// <typeparam name="TSelf">The type of the state.</typeparam>
public interface IImportJobState<TSelf>
    where TSelf : IImportJobState<TSelf>
{
    /// <summary>
    /// The state type, stored in the database so that we can identify the type of the state object.
    /// </summary>
    public static abstract string StateType { get; }

    /// <summary>
    /// Read and convert the JSON to <typeparamref name="TSelf"/>.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> to read from.</param>
    /// <param name="stateType">The state-type stored in the database.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> being used.</param>
    /// <returns>The value that was converted.</returns>
    public static virtual TSelf? Read(ref Utf8JsonReader reader, string stateType, JsonSerializerOptions options)
    {
        if (stateType == TSelf.StateType)
        {
            return JsonSerializer.Deserialize<TSelf>(ref reader, options);
        }

        return default;
    }
}
