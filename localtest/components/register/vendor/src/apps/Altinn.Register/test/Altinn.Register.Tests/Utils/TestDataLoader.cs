using System.Buffers;
using System.Text.Json;
using Nerdbank.Streams;

namespace Altinn.Register.Tests.Utils;

public static class TestDataLoader
{
    public static async Task<T> Load<T>(string id)
    {
        using var content = await LoadContent(id);

        return await JsonSerializer.DeserializeAsync<T>(content.AsReadOnlySequence.AsStream())
            ?? throw new InvalidDataException($"Test data '{id}' could not be deserialized as {typeof(T).FullName}.");
    }

    public static async Task<Sequence<byte>> LoadContent(string id)
    {
        Sequence<byte>? content = new(ArrayPool<byte>.Shared);

        try
        {
            using var fs = File.OpenRead(GetPath(id));
            await fs.CopyToAsync(content.AsStream());

            var ret = content;
            content = null;
            return ret;
        }
        finally
        {
            content?.Dispose();
        }
    }

    private static string GetPath(string id)
    {
        string unitTestFolder = Path.GetDirectoryName(new Uri(typeof(TestDataLoader).Assembly.Location).LocalPath)!;
        return Path.Combine(unitTestFolder, "Testdata", $"{id}.json");
    }
}
