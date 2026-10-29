using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The installation's database passwords: the file (<see cref="PostgresPasswords" />), the manifest's placeholders and
/// roles, and the reconciliation in the Postgres container, which must be studioctl's byte for byte (its golden file's
/// <c>reconcile</c>).
/// </summary>
public sealed partial class PasswordTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("postgres-passwords-").FullName;

    [GeneratedRegex("^[0-9a-f]{48}$", RegexOptions.CultureInvariant)]
    private static partial Regex Generated();

    private static readonly JsonElement _goldenFile = LoadGolden();

    private static JsonElement LoadGolden()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "environment.golden.json"))
        );
        return document.RootElement.Clone();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PasswordFile() => PostgresPasswords.PathOf(Path.Combine(_directory, "data"));

    [Fact]
    public void TheFileIsInTheSecretsDirectoryOfTheDataDirectory() =>
        Assert.Equal(Path.Combine("/data", "secrets", "postgres-passwords.json"), PostgresPasswords.PathOf("/data"));

    [Fact]
    public void AMissingFileGetsARandomPasswordPerRoleOwnerOnly()
    {
        var got = PostgresPasswords.Ensure(PasswordFile(), ["a", "b"]);

        Assert.Equal(2, got.Count);
        Assert.Matches(Generated(), got["a"]);
        Assert.Matches(Generated(), got["b"]);
        Assert.NotEqual(got["a"], got["b"]);
        Assert.Equal(PostgresPasswords.Serialize(got), File.ReadAllBytes(PasswordFile()));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(PasswordFile())!)); // no temporary file left
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PasswordFile()));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(PasswordFile())!)
            );
        }
    }

    [Fact]
    public void StoredPasswordsAreKeptAndMissingOnesAdded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile())!);
        File.WriteAllText(PasswordFile(), """{"a": "kept-value-0123456789", "old": "a-role-no-longer-asked"}""");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                PasswordFile(),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead
            );
        }

        var got = PostgresPasswords.Ensure(PasswordFile(), ["a", "b"]);
        var again = PostgresPasswords.Ensure(PasswordFile(), ["a", "b"]);

        Assert.Equal("kept-value-0123456789", got["a"]);
        Assert.Matches(Generated(), got["b"]);
        Assert.Equal(got["b"], again["b"]);
        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PasswordFile()))!;
        Assert.Equal("a-role-no-longer-asked", stored["old"]);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PasswordFile()));
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""["a"]""")]
    [InlineData("""{"a": "short"}""")]
    [InlineData("""{"a": "with@at;semicolon-0123"}""")]
    [InlineData("""{"a": "it's-0123456789abcdef"}""")]
    public void AnUnusableFileIsRefusedNotOverwritten(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile())!);
        File.WriteAllText(PasswordFile(), content);

        Assert.Throws<InvalidDataException>(() => PostgresPasswords.Ensure(PasswordFile(), ["a"]));
        Assert.Equal(content, File.ReadAllText(PasswordFile()));
    }

    /// <summary>The bytes studioctl's json.MarshalIndent writes for the same map.</summary>
    [Fact]
    public void TheFileHasStudioctlsFormat() =>
        Assert.Equal(
            "{\n  \"a\": \"0123456789abcdef\",\n  \"b\": \"fedcba9876543210\"\n}\n",
            Encoding.UTF8.GetString(
                PostgresPasswords.Serialize(
                    new Dictionary<string, string> { ["b"] = "fedcba9876543210", ["a"] = "0123456789abcdef" }
                )
            )
        );

    [Fact]
    public void NewPasswordsDiffer()
    {
        var seen = Enumerable.Range(0, 100).Select(_ => PostgresPasswords.NewPassword()).ToHashSet();
        Assert.Equal(100, seen.Count);
        Assert.All(seen, p => Assert.Matches(Generated(), p));
    }

    [Fact]
    public void TheRolesAreStudioctls() =>
        Assert.Equal(
            _goldenFile.GetProperty("passwords").EnumerateObject().Select(p => p.Name),
            LocaltestManifest.Default.RoleNames
        );

    [Fact]
    public void TheReconciliationIsStudioctls()
    {
        var reconcile = _goldenFile.GetProperty("reconcile");
        var manifest = LocaltestManifest.Default;
        Assert.Equal(reconcile.GetProperty("script").GetString(), manifest.ReconcileScript);
        Assert.Equal(
            reconcile.GetProperty("command").EnumerateArray().Select(a => a.GetString()),
            manifest.ReconcileCommand
        );
        Assert.Equal(
            ["exec", "-i", "postgres-abc", .. manifest.ReconcileCommand],
            new PasswordReconciliation(manifest, () => null).Arguments("postgres-abc")
        );
        Assert.DoesNotContain("{{", manifest.ReconcileScript, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureIsReportedWithThePasswordsMasked()
    {
        var reconciliation = new PasswordReconciliation(
            LocaltestManifest.Default,
            () => new Dictionary<string, string> { ["platform_storage"] = "0123456789abcdef0123" }
        );
        Assert.Equal("near '***'", reconciliation.Masked("near '0123456789abcdef0123'"));
    }

    [Fact]
    public void OnlyPostgresHealthCheckReconciles()
    {
        var model = SampleModel.Build([]);
        var checks = model.HealthChecks();
        Assert.NotNull(checks[LocaltestManifest.PostgresContainer].Reconciliation);
        Assert.All(
            checks.Where(c => c.Key != LocaltestManifest.PostgresContainer),
            c => Assert.Null(c.Value.Reconciliation)
        );
    }

    [Fact]
    public void ThePlaceholdersResolveOnceThePasswordsAreRead()
    {
        var model = SampleModel.Build([], dataDirectory: Path.Combine(_directory, "data"));
        var stack = model.Platform.Resource.Stack;
        var storage = () => stack.Specs().Single(s => s.Name == "storage").Environment;
        Assert.Equal("{{password:platform_storage}}", storage()["PostgreSqlSettings__StorageDbPwd"].Text);

        stack.EnsurePasswords();

        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PasswordFile()))!;
        Assert.Equal(LocaltestManifest.Default.RoleNames, stored.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(stored["platform_storage"], storage()["PostgreSqlSettings__StorageDbPwd"].Text);
        var postgres = stack.Specs().Single(s => s.Name == LocaltestManifest.PostgresContainer).Environment;
        Assert.Equal(stored["platform_storage"], postgres["PLATFORM_STORAGE_PASSWORD"].Text);
        Assert.Equal("Password", postgres["POSTGRES_PASSWORD"].Text);
    }

    private const string WithPasswords = """
        {
          "groups": [{ "name": "default", "containers": ["web"] }],
          "public": { "host": "example.test", "port": 8000, "subdomains": [] },
          "containers": {
            "postgres": { "registry": "example.org/db", "tag": "1", "port": 5432, "health": "tcp",
                          "envs": { "POSTGRES_USER": "postgres", "POSTGRES_PASSWORD": "Password",
                                    "WEB_PASSWORD": "{{password:web}}" } },
            "web": { "registry": "example.org/web", "tag": "1", "envs": { "PWD": "PWD_VALUE" },
                     "dependsOn": ["postgres"] }
          }
        }
        """;

    [Fact]
    public void AManifestWithAPasswordOfACarriedRoleIsValid()
    {
        var manifest = LocaltestManifest.Parse(
            Encoding.UTF8.GetBytes(WithPasswords.Replace("PWD_VALUE", "{{password:web}}", StringComparison.Ordinal))
        );
        Assert.Equal([new ManifestRole("web", "WEB_PASSWORD")], manifest.Roles);
    }

    [Theory]
    [InlineData("\"{{password:nobody}}\"", "", "no variable of container postgres is {{password:nobody}}")]
    [InlineData("\"{{password:Web-Role}}\"", "", "is not a role name")]
    [InlineData("\"{{password:web}}\"", "\"SECOND_PASSWORD\": \"{{password:web}}\",", "carries web in both")]
    [InlineData("\"{{password:web}}\"", "\"POSTGRES_PASSWORD\": \"{{password:root}}\",", "needs a literal")]
    public void ABadPasswordPlaceholderIsAManifestError(string value, string postgresExtra, string fault)
    {
        ArgumentNullException.ThrowIfNull(postgresExtra);
        var json = WithPasswords
            .Replace("\"PWD_VALUE\"", value, StringComparison.Ordinal)
            .Replace("\"WEB_PASSWORD\"", postgresExtra + " \"WEB_PASSWORD\"", StringComparison.Ordinal);
        if (postgresExtra.Contains("POSTGRES_PASSWORD", StringComparison.Ordinal))
        {
            json = json.Replace("\"POSTGRES_PASSWORD\": \"Password\",", "", StringComparison.Ordinal);
        }
        var error = Assert.Throws<InvalidDataException>(() => LocaltestManifest.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
    }
}
