using System.Text.Json;
using System.Text.RegularExpressions;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Integrations.Ccr.Xml;
using Altinn.Register.Persistence.ImportJobs;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Nerdbank.Streams;
using Xunit.Sdk;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="CcrXmlProcessor"/>
/// </summary>
public partial class CcrXmlProcessorTests
    : DatabaseTestBase
{
    private static readonly JsonSerializerOptions _options
        = PostgresSagaStatePersistence.JsonSerializerOptions;

    [Theory]
    [MemberData(nameof(TestCases))]
    public async Task VerifyXmlProcessor(TestCase testCase)
    {
        // var logger = new NullLogger<CcrFlatFileProcessor>();
        using var seq = new Sequence<byte>();

        {
            await using var stream = testCase.OpenRead();
            await stream.CopyToAsync(seq.AsStream(), CancellationToken);
        }

        var locationLookupProvider = GetRequiredService<ILocationLookupProvider>();
        var externalRoleDefinitionLookupProvider = GetRequiredService<IExternalRoleDefinitionPersistence>();

        var locationLookup = await locationLookupProvider.GetLocationLookup(CancellationToken);
        var externalRoleDefinitionLookup = await externalRoleDefinitionLookupProvider.GetRoleDefinitionLookup(CancellationToken);

        var processor = new CcrXmlProcessor();
        var updates = processor.ProcessCcrXml(
            seq.AsReadOnlySequence,
            externalRoleDefinitionLookup,
            locationLookup,
            CancellationToken);

        var json = JsonSerializer.Serialize(updates, _options);
        await VerifyJson(json)
            .UseParameters(testCase.ToString());
    }

    public static TheoryData<TestCase> TestCases()
    {
        var contents = TestCase.FileProvider.GetDirectoryContents(".");
        var parentRegex = TestCase.GetParentTestNameRegex();

        var data = new TheoryData<TestCase>();
        foreach (var dir in contents
            .Where(static e => e.IsDirectory))
        {
            var match = parentRegex.Match(dir.Name);
            if (!match.Success)
            {
                continue;
            }

            var parentName = match.Groups["parent"].Value;
            var parentContents = TestCase.FileProvider.GetDirectoryContents(dir.Name);
            foreach (var file in parentContents
                .Where(static e => !e.IsDirectory && e.Name.EndsWith(".xml")))
            {
                var testCase = new TestCase { Parent = parentName, Name = file.Name[..^4], Directory = dir.Name };
                var row = new TheoryDataRow<TestCase>(testCase);

                if (parentName == "test2")
                {
                    // known issue, we're investigating
                    row.Skip = "Bad data?";
                }

                data.Add(row);
            }
        }

        return data;
    }

    public sealed partial record TestCase
        : IXunitSerializable
    {
        public static readonly TestDataFileProvider FileProvider = TestDataFileProvider.Snapshots;

        [GeneratedRegex(@"^.*?_testCase=(?<parent>.*?)\.verified$")]
        public static partial Regex GetParentTestNameRegex();

        private string _directory = null!;
        private string _parent = null!;
        private string _name = null!;

        public required string Name
        {
            get => _name;
            init => _name = value;
        }

        public required string Parent
        {
            get => _parent;
            init => _parent = value;
        }

        public required string Directory
        {
            get => _directory;
            init => _directory = value;
        }

        public override string ToString()
            => $"{Parent}/{Name}";

        public Stream OpenRead()
        {
            var file = FileProvider.GetFileInfo($"{Directory}/{Name}.xml");

            file.ShouldNotBeNull();
            return file.CreateReadStream();
        }

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            _name = info.GetValue<string>(nameof(Name))!;
            _parent = info.GetValue<string>(nameof(Parent))!;
            _directory = info.GetValue<string>(nameof(Directory))!;
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Name), _name);
            info.AddValue(nameof(Parent), _parent);
            info.AddValue(nameof(Directory), _directory);
        }
    }
}
