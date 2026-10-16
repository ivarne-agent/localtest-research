using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using Altinn.Authorization.TestUtils.Shouldly;

namespace Altinn.Register.Contracts.Tests;

public abstract class JsonModelTests
{
    protected static JsonSerializerOptions Options { get; } = JsonSerializerOptions.Web;

    private static JsonWriterOptions WriterOptions { get; }
        = new JsonWriterOptions
        {
            Indented = true,
            SkipValidation = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    protected async ValueTask ValidateJson<T>(
        T value,
        [StringSyntax(StringSyntaxAttribute.Json)] string json,
        bool snapshot = true)
        where T : class
    {
        using var doc = JsonSerializer.SerializeToDocument(value, Options);

        doc.RootElement.ShouldBeStructurallyEquivalentTo(json);

        var deserialized = JsonSerializer.Deserialize<T>(doc, Options);
        deserialized.ShouldNotBeNull();
        deserialized.ShouldBe(value);

        if (snapshot)
        {
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, WriterOptions);
            doc.WriteTo(writer);
            writer.Flush();
            ms.WriteByte((byte)'\n'); // insert final newline

            await Verify(ms, extension: "json");
        }
    }
}
