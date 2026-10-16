using Altinn.Register.Integrations.Ccr.FileImport;
using Altinn.Register.Tests.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Nerdbank.Streams;
using Xunit.Sdk;

namespace Altinn.Register.Tests.UnitTests;

public class CcrFlatFileProcessorTests
{
    [Theory]
    [MemberData(nameof(TestCases))]
    public async Task VerifyFlatFileProcessor(TestCase testCase)
    {
        var logger = new NullLogger<CcrFlatFileProcessor>();
        await using var stream = testCase.OpenRead();
        var reader = stream.UsePipeReader(cancellationToken: TestContext.Current.CancellationToken);

        var results = await CcrFlatFileProcessor.ProcessAsync(logger, reader, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        await Verify(results)
            .UseUniqueDirectory()
            .UseParameters(testCase.Name);
    }

    public static TheoryData<TestCase> TestCases()
    {
        var contents = TestCase.FileProvider.GetDirectoryContents(".");

        var data = new TheoryData<TestCase>();
        foreach (var file in contents
            .Where(static e => !e.IsDirectory && e.Name.EndsWith(".txt"))
            .OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            data.Add(new TestCase { Name = file.Name[..^4] });
        }

        return data;
    }

    public sealed record TestCase
        : IXunitSerializable
    {
        public static readonly TestDataFileProvider FileProvider = TestDataFileProvider.For("Ccr/FlatFile");

        private string _name = null!;

        public required string Name
        {
            get => _name;
            init => _name = value;
        }

        public override string ToString()
            => Name;

        public Stream OpenRead()
        {
            var file = FileProvider.GetFileInfo($"{Name}.txt");

            file.ShouldNotBeNull();
            return file.CreateReadStream();
        }

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            _name = info.GetValue<string>(nameof(Name))!;
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Name), _name);
        }
    }
}
