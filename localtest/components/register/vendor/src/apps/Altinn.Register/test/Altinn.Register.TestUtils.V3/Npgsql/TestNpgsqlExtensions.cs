using System.Data;
using System.Text.Json;
using Npgsql;

namespace Altinn.Register.TestsUtils.Npgsql;

/// <summary>
/// Test helpers for npgsql.
/// </summary>
public static class TestNpgsqlExtensions
{
    /// <summary>
    /// Gets field value as json and deserializes it.
    /// </summary>
    public static T? GetJsonFieldValue<T>(this NpgsqlDataReader reader, int ordinal, JsonSerializerOptions? options = null)
    {
        var data = reader.GetFieldValue<ReadOnlyMemory<byte>>(ordinal);
        return JsonSerializer.Deserialize<T>(data.Span, options ?? JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Gets field value as json and deserializes it.
    /// </summary>
    public static T? GetJsonFieldValue<T>(this NpgsqlDataReader reader, string fieldName, JsonSerializerOptions? options = null)
    {
        var data = reader.GetFieldValue<ReadOnlyMemory<byte>>(fieldName);
        return JsonSerializer.Deserialize<T>(data.Span, options ?? JsonSerializerOptions.Web);
    }
}
