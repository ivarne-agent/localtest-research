using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Altinn.App.Aspire;

/// <summary>
/// The installation's database passwords, as studioctl keeps its own (<c>internal/localtestv2/passwords.go</c>): one
/// random password per role of the manifest, in a JSON object of role name to password,
/// <c>secrets/postgres-passwords.json</c> under the package's data directory, readable by the owner only. A role
/// without one gets a new one, saved before anything starts; a stored one is never changed. The two runners have
/// separate volumes, so each keeps its own file, in the same format.
/// </summary>
internal static partial class PostgresPasswords
{
    /// <summary>The file's name.</summary>
    public const string FileName = "postgres-passwords.json";

    /// <summary>The directory under the data directory that holds the file.</summary>
    public const string DirectoryName = "secrets";

    /// <summary>The entropy of a generated password: 192 bits, written as 48 lower-case hex digits.</summary>
    private const int PasswordBytes = 24;

    /// <summary>
    /// A password the file may hold: what is safe in every form the services read it in (connection strings, a
    /// <c>postgres://</c> URL) without quoting, at least as long as 64 bits of hex.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{16,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredPassword();

    /// <summary>The file under the package's data directory.</summary>
    public static string PathOf(string dataDirectory) => Path.Combine(dataDirectory, DirectoryName, FileName);

    /// <summary>A random password: 192 bits from the system's random source, in lower-case hex.</summary>
    public static string NewPassword() =>
#pragma warning disable CA1308 // The hex digits are lower-case, as studioctl's.
        Convert.ToHexString(RandomNumberGenerator.GetBytes(PasswordBytes)).ToLowerInvariant();
#pragma warning restore CA1308

    /// <summary>
    /// Reads the file at <paramref name="path" /> and returns a password for every role: the stored one, or a new one,
    /// which it saves (atomically, owner-only, in an owner-only directory) first. Roles the file holds that are not
    /// asked for are kept.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a JSON object of usable passwords.</exception>
    public static IReadOnlyDictionary<string, string> Ensure(string path, IReadOnlyList<string> roles)
    {
        var stored = Read(path);
        var changed = false;
        foreach (var role in roles)
        {
            if (stored.TryAdd(role, NewPassword()))
            {
                changed = true;
            }
        }
        if (changed)
        {
            Write(path, stored);
        }
        else if (File.Exists(path))
        {
            Restrict(path);
        }
        return roles.ToDictionary(r => r, r => stored[r], StringComparer.Ordinal);
    }

    private static SortedDictionary<string, string> Read(string path)
    {
        if (!File.Exists(path))
        {
            return new SortedDictionary<string, string>(StringComparer.Ordinal);
        }
        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(
                File.ReadAllBytes(path),
                PasswordsJsonContext.Default.DictionaryStringString
            );
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                $"bad database password file {path}: {e.Message} (delete it to generate new passwords; the next start sets them)",
                e
            );
        }
        var stored = new SortedDictionary<string, string>(
            parsed ?? throw new InvalidDataException($"bad database password file {path}: not an object"),
            StringComparer.Ordinal
        );
        if (
            stored.FirstOrDefault(kv => kv.Value is null || !StoredPassword().IsMatch(kv.Value)) is
            { Key: not null } bad
        )
        {
            throw new InvalidDataException(
                $"bad database password file {path}: the password of {bad.Key} is not 16 to 256 letters, digits, - or _ "
                    + "(delete the entry to generate a new one)"
            );
        }
        return stored;
    }

    /// <summary>The bytes studioctl writes for the same passwords: Go's indented JSON, keys sorted, a final newline.</summary>
    internal static byte[] Serialize(IReadOnlyDictionary<string, string> passwords)
    {
        using var buffer = new MemoryStream();
        using (
            var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = true,
                    NewLine = "\n",
                    IndentSize = 2,
                }
            )
        )
        {
            writer.WriteStartObject();
            foreach (var (role, password) in passwords.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                writer.WriteString(role, password);
            }
            writer.WriteEndObject();
        }
        buffer.Write("\n"u8);
        return buffer.ToArray();
    }

    private static void Write(string path, IReadOnlyDictionary<string, string> passwords)
    {
        var directory = Path.GetDirectoryName(path)!;
        EnsureOwnerOnlyDirectory(directory);
        var temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(temporary, create))
            {
                stream.Write(Serialize(passwords));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        EnsureOwnerOnlyDirectory(Path.GetDirectoryName(path)!);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void EnsureOwnerOnlyDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(directory, ownerOnly);
        File.SetUnixFileMode(directory, ownerOnly);
    }
}

/// <summary>Source-generated JSON for the password file: an object of strings.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class PasswordsJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
