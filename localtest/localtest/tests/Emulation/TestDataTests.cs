using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Register.Localtest.TestData;
using Localtest.Emulated;
using Microsoft.Extensions.Logging.Abstractions;

namespace Localtest.Tests.Emulation;

/// <summary>
/// The minimal mode's test data: the copies of the Register fake's file format and seed (the Docker build context is
/// localtest/localtest only, so the image cannot link them), and the store's merge rules.
/// </summary>
public sealed class TestDataTests
{
    /// <summary>Each copy in Emulation/TestData and its original, relative to the repository's localtest folder.</summary>
    public static TheoryData<string, string> Copies => new()
    {
        { "localtest/Emulation/TestData/TestDataFile.cs", "components/register/fakes/Altinn.Register.Localtest/TestData/TestDataFile.cs" },
        { "localtest/Emulation/TestData/TestDataMerger.cs", "components/register/fakes/Altinn.Register.Localtest/TestData/TestDataMerger.cs" },
        { "localtest/Emulation/TestData/testData.json", "components/register/testdata/testData.json" },
    };

    [Theory]
    [MemberData(nameof(Copies))]
    public void TheCopiesAreTheRegistersFiles(string copy, string original)
    {
        var root = LocaltestFolder();
        var copied = File.ReadAllBytes(Path.Combine(root, copy));
        var source = File.ReadAllBytes(Path.Combine(root, original));
        Assert.True(
            copied.AsSpan().SequenceEqual(source),
            $"localtest/{copy} differs from localtest/{original}. The minimal mode keeps a byte-identical copy of the Register's "
            + $"test-data files (localtest/localtest/Emulation/README.md, \"Test data\"): after changing the original, copy it "
            + $"over the copy (cp localtest/{original} localtest/{copy}); never edit the copy alone.");
    }

    [Fact]
    public void TheSourceGeneratedFormatWritesWhatTheReflectionSerializerWrote()
    {
        // The Register's files moved from reflection to a source-generated context (so they compile under AOT); the
        // canonical JSON that tells identical duplicates from conflicts must not change with it.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var seed = File.ReadAllText(Path.Combine(LocaltestFolder(), "components/register/testdata/testData.json"));
        var data = TestDataFile.Parse(seed, "seed");
        var reflected = JsonSerializer.Deserialize<TestDataFile>(seed, options)!;

        Assert.Equal(JsonSerializer.Serialize(reflected, options), JsonSerializer.Serialize(data, options));
        Assert.All(data.Persons, p => Assert.Equal(JsonSerializer.Serialize(p, options), Canonical(p)));
        Assert.All(data.Orgs, o => Assert.Equal(JsonSerializer.Serialize(o, options), Canonical(o)));
        Assert.All(data.Systems, s => Assert.Equal(JsonSerializer.Serialize(s, options), Canonical(s)));
    }

    [Fact]
    public void TheSeedLoadsAsTheRegisterLoadsIt()
    {
        var data = new TestDataStore(NullLogger<TestDataStore>.Instance).Current;

        Assert.Equal("seed+apps", data.Source);
        Assert.Equal(8, data.Users.Count);
        Assert.Equal(13, data.ByPartyId.Values.Count(p => p.Kind == PartyKind.Organisation));
        Assert.Single(data.ByPartyId.Values, p => p.Kind == PartyKind.SystemUser);
        Assert.Empty(data.Conflicts);
        Assert.Equal("Sophie Salt", data.UsersByUserId[1337].Party.Name);
        Assert.Equal(PartyKind.SelfIdentified, data.UsersByUserId[1004].Party.Kind);
        Assert.Equal([500001, 500002, 500003], data.SubUnitsOf(500000).Select(p => p.PartyId));
        Assert.Contains(data.ActingFor(data.UsersByUserId[1337]), p => p.PartyId == 500001);
    }

    [Fact]
    public void AppsOnlyWhenEveryAppHasTestDataAndDeprovisioningRemovesTheApps()
    {
        var store = new TestDataStore(NullLogger<TestDataStore>.Instance);
        const string App = """{"persons":[{"ssn":"01899699552","userId":1001,"partyId":510001,"firstName":"Pengelens","lastName":"Partner","partyRoles":{}}, {"ssn":"","userId":7001,"partyId":570001,"email":"app@example.com","partyRoles":{}}],"orgs":[]}""";

        Assert.Equal("apps", store.Register("ttd", "with-data", System.Text.Encoding.UTF8.GetBytes(App)).Source);
        Assert.Equal([1001, 7001], store.Current.Users.Select(u => u.UserId).Order());

        var mixed = store.Register("ttd", "without-data", null);
        Assert.Equal("seed+apps", mixed.Source);
        Assert.Contains(mixed.Users, u => u.UserId == 1337);
        Assert.Contains(mixed.Users, u => u.UserId == 7001);
        // The app's Pengelens Partner differs from the seed's: a conflict, the seed's definition wins.
        Assert.Contains(mixed.Conflicts, c => c.Contains("userId 1001", StringComparison.Ordinal));

        Assert.True(store.Unregister("ttd", "with-data"));
        Assert.DoesNotContain(store.Current.Users, u => u.UserId == 7001);
        Assert.True(store.Unregister("ttd", "without-data"));
        Assert.False(store.Unregister("ttd", "without-data"));
        Assert.Equal(8, store.Current.Users.Count);
    }

    [Fact]
    public void AccessPackagesOnTheOwnPartyAreNotAGrant()
    {
        var store = new TestDataStore(NullLogger<TestDataStore>.Instance);
        store.Register("ttd", "own", System.Text.Encoding.UTF8.GetBytes("""{"persons":[{"ssn":"","userId":7001,"partyId":570001,"email":"a@example.com","partyRoles":{},"accessPackages":{"570001":["lonn"]}}],"orgs":[]}"""));

        Assert.Empty(store.Current.UsersByUserId[7001].Grants);
    }

    [Theory]
    [InlineData("01899699552", true)]
    [InlineData("01899699553", false)]
    [InlineData("0189969955", false)]
    public void PersonIdentifiersAreCheckedAsTheRegisterChecksThem(string ssn, bool valid)
        => Assert.Equal(valid, TestDataSnapshot.IsPersonIdentifier(ssn));

    [Theory]
    [InlineData("897069644", true)]
    [InlineData("897069645", false)]
    [InlineData("900000006", true)]
    public void OrganisationNumbersAreCheckedAsTheRegisterChecksThem(string number, bool valid)
        => Assert.Equal(valid, TestDataSnapshot.IsOrganisationNumber(number));

    private static string Canonical<T>(T value)
        => (string)typeof(TestDataFile).GetMethod("Canonical", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(typeof(T)).Invoke(null, [value])!;

    /// <summary>The repository's localtest folder, found upwards from the test binaries.</summary>
    internal static string LocaltestFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "components", "register")) && Directory.Exists(Path.Combine(directory.FullName, "localtest")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No localtest folder above {AppContext.BaseDirectory}.");
    }
}
