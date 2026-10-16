using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Authorization.TestUtils;
using Altinn.Authorization.TestUtils.Shouldly;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Npr;
using Altinn.Register.Integrations.Npr.Person;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Xunit.Sdk;

namespace Altinn.Register.Tests.UnitTests;

public class NprPersonParsingTests
    : DatabaseTestBase
{
    private static readonly TestDataFileProvider PersonsFileProvider = TestDataFileProvider.For("Npr/Persons");

    private ILocationLookupProvider LookupProvider
        => GetRequiredService<ILocationLookupProvider>();

    [Theory]
    [MemberData(nameof(ValidPersonCases))]
    public async Task ParsePersonDocument(ValidPersonCase validPersonCase)
    {
        var lookup = await LookupProvider.GetLocationLookup(TestContext.Current.CancellationToken);
        var validator = new PersonDocumentValidator(lookup);

        using var source = await validPersonCase.ReadNprJson();

        var parsed = Json.Deserialize<PersonDocument>(source);
        parsed.ShouldNotBeNull();

        ValidationProblemBuilder builder = default;
        builder.TryValidate(path: "/", parsed, validator, out NprPerson? person);

        if (builder.TryBuild(out var error))
        {
            throw new ProblemInstanceException(error);
        }

        person.ShouldNotBeNull();
        using var serializedPerson = Json.SerializeToDocument(person);

        using var validatedPerson = await validPersonCase.ReadValidatedJson();

        try
        {
            validatedPerson.ShouldNotBeNull($"Validated JSON not found for case '{validPersonCase.Name}'.");

            serializedPerson.ShouldBeStructurallyEquivalentTo(validatedPerson);

            validPersonCase.DeleteReceivedJsons();
        }
        catch
        {
            await validPersonCase.WriteReceivedJson(serializedPerson);
            throw;
        }
    }

    public static TheoryData<ValidPersonCase> ValidPersonCases()
    {
        var contents = PersonsFileProvider.GetDirectoryContents("./valid");

        var data = new TheoryData<ValidPersonCase>();
        foreach (var dir in contents.Where(static e => e.IsDirectory))
        {
            data.Add(new ValidPersonCase { Name = dir.Name });
        }

        return data;
    }

    public sealed record ValidPersonCase
        : IXunitSerializable
    {
        private const string SOURCE_FILE_NAME = "npr.json";
        private const string VALIDATED_RESULT_FILE_NAME = "result.validated.json";
        private const string RECEIVED_RESULT_FILE_NAME = "result.received.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(Json.Options)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement),
        };

        private string _name = null!;

        public required string Name
        {
            get => _name;
            init => _name = value;
        }

        public override string ToString()
            => Name;

        public async Task<JsonDocument> ReadNprJson()
        {
            await using var fs = PersonsFileProvider.GetFileInfo($"./valid/{Name}/{SOURCE_FILE_NAME}").CreateReadStream();
            return await JsonDocument.ParseAsync(fs, cancellationToken: TestContext.Current.CancellationToken);
        }

        public Task<JsonDocument?> ReadValidatedJson()
            => ReadValidatedJson(VALIDATED_RESULT_FILE_NAME);

        public Task WriteReceivedJson(JsonDocument document)
            => WriteReceivedJson(document, RECEIVED_RESULT_FILE_NAME);

        public void DeleteReceivedJsons()
        {
            var personsDir = FindPersonsDir();
            var caseDir = Path.Join(personsDir, "valid", Name);

            TryDelete(Path.Join(caseDir, RECEIVED_RESULT_FILE_NAME));

            static void TryDelete(string path)
            {
                try
                {
                    File.Delete(path);
                }
                catch (FileNotFoundException)
                {
                    // ignore
                }
            }
        }

        private async Task<JsonDocument?> ReadValidatedJson(string filename)
        {
            var fileInfo = PersonsFileProvider.GetFileInfo($"./valid/{Name}/{filename}");
            if (!fileInfo.Exists)
            {
                return null;
            }

            await using var fs = fileInfo.CreateReadStream();
            return await JsonDocument.ParseAsync(fs, cancellationToken: TestContext.Current.CancellationToken);
        }

        private async Task WriteReceivedJson(JsonDocument document, string filename)
        {
            var personsDir = FindPersonsDir();
            var caseDir = Path.Join(personsDir, "valid", Name);

            await using var fs = new FileStream(Path.Join(caseDir, filename), FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, document, Options, cancellationToken: TestContext.Current.CancellationToken);
            await fs.WriteAsync("\n"u8.ToArray(), cancellationToken: TestContext.Current.CancellationToken);
        }

        private static string FindPersonsDir()
        {
            var projectFile = FindUp("Altinn.Register.Tests.csproj", Environment.CurrentDirectory);
            var testDataDir = Path.Join(Path.GetDirectoryName(projectFile), "Testdata");
            var personsDir = Path.Join(testDataDir, "Npr", "Persons");

            return personsDir;

            static string FindUp(string relativePath, string current)
            {
                for (var i = 0; i < 20; i++)
                {
                    var candidate = Path.Join(current, relativePath);
                    if (Directory.Exists(candidate) || File.Exists(candidate))
                    {
                        return candidate;
                    }

                    var parent = Directory.GetParent(current);
                    if (parent is null)
                    {
                        throw new DirectoryNotFoundException($"Could not find path '{relativePath}' starting from '{current}'.");
                    }

                    current = parent.FullName;
                }

                throw new DirectoryNotFoundException($"Could not find path '{relativePath}' starting from '{current}'. Maximum search depth exceeded.");
            }
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
