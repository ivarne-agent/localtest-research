using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Authorization.TestUtils;
using Altinn.Authorization.TestUtils.Shouldly;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire.Organization;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Xunit.Sdk;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Golden-file tests for <see cref="OrganizationDocumentValidator"/> over real SIRE responses.
/// </summary>
/// <remarks>
/// To add a new fixture, capture a raw SIRE response via the debug controller
/// (<c>GET /register/api/v0/debug/sire/raw/{orgNo}</c>) and save it as
/// <c>Testdata/Sire/Organizations/valid/{orgNo}/sire.json</c>. On first test run the validator's
/// serialized output will be written to <c>result.received.json</c>; inspect it, and if correct,
/// rename it to <c>result.validated.json</c> to accept it as the snapshot. Subsequent runs compare
/// to that snapshot and write a fresh <c>result.received.json</c> on mismatch.
/// </remarks>
public class SireOrganizationParsingTests
    : DatabaseTestBase
{
    private static readonly TestDataFileProvider OrganizationsFileProvider
        = TestDataFileProvider.For("Sire/Organizations");

    private ILocationLookupProvider LookupProvider
        => GetRequiredService<ILocationLookupProvider>();

    [Theory]
    [MemberData(nameof(ValidOrganizationCases))]
    public async Task ParseOrganizationDocument(ValidOrganizationCase validCase)
    {
        var lookup = await LookupProvider.GetLocationLookup(TestContext.Current.CancellationToken);
        var validator = new OrganizationDocumentValidator(lookup, DateTimeOffset.UtcNow);

        using var source = await validCase.ReadSireJson();

        var parsed = Json.Deserialize<OrganizationDocument>(source);
        parsed.ShouldNotBeNull();

        ValidationProblemBuilder builder = default;
        builder.TryValidate(path: "/", parsed, validator, out SireOrganization? organization);

        if (builder.TryBuild(out var error))
        {
            throw new ProblemInstanceException(error);
        }

        organization.ShouldNotBeNull();
        using var serializedOrg = Json.SerializeToDocument(organization);

        using var validatedOrg = await validCase.ReadValidatedJson();

        try
        {
            validatedOrg.ShouldNotBeNull($"Validated JSON not found for case '{validCase.Name}'.");

            serializedOrg.ShouldBeStructurallyEquivalentTo(validatedOrg);

            validCase.DeleteReceivedJsons();
        }
        catch
        {
            await validCase.WriteReceivedJson(serializedOrg);
            throw;
        }
    }

    public static TheoryData<ValidOrganizationCase> ValidOrganizationCases()
    {
        var contents = OrganizationsFileProvider.GetDirectoryContents("./valid");

        var data = new TheoryData<ValidOrganizationCase>();
        foreach (var dir in contents.Where(static e => e.IsDirectory))
        {
            data.Add(new ValidOrganizationCase { Name = dir.Name });
        }

        return data;
    }

    public sealed record ValidOrganizationCase
        : IXunitSerializable
    {
        private const string SOURCE_FILE_NAME = "sire.json";
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

        public async Task<JsonDocument> ReadSireJson()
        {
            await using var fs = OrganizationsFileProvider.GetFileInfo($"./valid/{Name}/{SOURCE_FILE_NAME}").CreateReadStream();
            return await JsonDocument.ParseAsync(fs, cancellationToken: TestContext.Current.CancellationToken);
        }

        public Task<JsonDocument?> ReadValidatedJson()
            => ReadValidatedJson(VALIDATED_RESULT_FILE_NAME);

        public Task WriteReceivedJson(JsonDocument document)
            => WriteReceivedJson(document, RECEIVED_RESULT_FILE_NAME);

        public void DeleteReceivedJsons()
        {
            var organizationsDir = FindOrganizationsDir();
            var caseDir = Path.Join(organizationsDir, "valid", Name);

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
            var fileInfo = OrganizationsFileProvider.GetFileInfo($"./valid/{Name}/{filename}");
            if (!fileInfo.Exists)
            {
                return null;
            }

            await using var fs = fileInfo.CreateReadStream();
            return await JsonDocument.ParseAsync(fs, cancellationToken: TestContext.Current.CancellationToken);
        }

        private async Task WriteReceivedJson(JsonDocument document, string filename)
        {
            var organizationsDir = FindOrganizationsDir();
            var caseDir = Path.Join(organizationsDir, "valid", Name);

            await using var fs = new FileStream(Path.Join(caseDir, filename), FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, document, Options, cancellationToken: TestContext.Current.CancellationToken);
            await fs.WriteAsync("\n"u8.ToArray(), cancellationToken: TestContext.Current.CancellationToken);
        }

        private static string FindOrganizationsDir()
        {
            var projectFile = FindUp("Altinn.Register.Tests.csproj", Environment.CurrentDirectory);
            var testDataDir = Path.Join(Path.GetDirectoryName(projectFile), "Testdata");
            var organizationsDir = Path.Join(testDataDir, "Sire", "Organizations");

            return organizationsDir;

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
