using Microsoft.Extensions.FileProviders;

namespace Altinn.Register.IntegrationTests.Utils;

public sealed class TestDataFileProvider
    : ManifestEmbeddedFileProvider
{
    public static TestDataFileProvider Testdata { get; } = new("Testdata");

    public static TestDataFileProvider For(string root)
        => new($"Testdata/{root}");

    private TestDataFileProvider(string root)
        : base(typeof(TestDataFileProvider).Assembly, root)
    {
    }
}
