using System.Text;
using System.Text.RegularExpressions;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// One database role whose password the runner generates: its name, and the variable of the Postgres container that
/// carries the password.
/// </summary>
internal sealed record ManifestRole(string Name, string Variable);

/// <summary>
/// The database passwords, as studioctl's <c>manifest/passwords.go</c>: the manifest holds no role password, a value
/// names one by <c>{{password:&lt;role&gt;}}</c>, which the runner resolves from its own file
/// (<see cref="PostgresPasswords" />). The Postgres container's variable whose value is exactly a role's placeholder
/// carries that role's password (its init scripts create the role with it on an empty volume); the superuser is the
/// container's literal <c>POSTGRES_USER</c> with the fixed <c>POSTGRES_PASSWORD</c>, the recovery path. Once Postgres is
/// healthy and before its dependants start, the runner runs <see cref="ReconcileScript" /> in it.
/// </summary>
internal sealed partial class LocaltestManifest
{
    /// <summary>The stack's Postgres: its envs carry the role passwords, and the runner reconciles them in it.</summary>
    public const string PostgresContainer = "postgres";

    /// <summary>Names the superuser, in the Postgres container's envs.</summary>
    public const string SuperuserVariable = "POSTGRES_USER";

    /// <summary>Holds the superuser's fixed password, in the Postgres container's envs.</summary>
    public const string SuperuserPasswordVariable = "POSTGRES_PASSWORD";

    private const string PasswordPlaceholder = "password";

    [GeneratedRegex(@"\{\{([^{}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex AnyPlaceholder();

    /// <summary>A role a placeholder may name: a lower-case SQL identifier that needs no quoting.</summary>
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();

    /// <summary>The role of a placeholder's inner text (<c>password:&lt;role&gt;</c>), or null when it is not one.</summary>
    internal static string? PasswordRole(string inner) =>
        inner.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0 && inner[..colon] == PasswordPlaceholder
            ? inner[(colon + 1)..]
            : null;

    /// <summary>
    /// The roles whose passwords the runner generates, sorted by name: one per variable of the Postgres container whose
    /// value is exactly a role's placeholder.
    /// </summary>
    public IReadOnlyList<ManifestRole> Roles =>
        _containers.TryGetValue(PostgresContainer, out var postgres)
            ?
            [
                .. postgres
                    .Envs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => (kv.Key, Role: ExactPassword(kv.Value)))
                    .Where(x => x.Role is not null)
                    .Select(x => new ManifestRole(x.Role!, x.Key))
                    .OrderBy(r => r.Name, StringComparer.Ordinal),
            ]
            : [];

    /// <summary>The names of <see cref="Roles" />, sorted.</summary>
    public IReadOnlyList<string> RoleNames => [.. Roles.Select(r => r.Name)];

    /// <summary>The Postgres superuser's name.</summary>
    public string Superuser => _containers[PostgresContainer].Envs[SuperuserVariable];

    /// <summary>
    /// <paramref name="value" /> with every <c>{{password:&lt;role&gt;}}</c> replaced by the role's password; a role
    /// without one stays as it is, which the runner prevents by filling every role first.
    /// </summary>
    public static string ResolvePasswords(string value, IReadOnlyDictionary<string, string> passwords) =>
        AnyPlaceholder()
            .Replace(
                value,
                match =>
                    PasswordRole(match.Groups[1].Value) is { } role && passwords.TryGetValue(role, out var password)
                        ? password
                        : match.Value
            );

    /// <summary>
    /// The command the runner executes in the Postgres container, with <see cref="ReconcileScript" /> on its standard
    /// input: psql as the superuser over the container's Unix socket, which the image's <c>pg_hba.conf</c> trusts
    /// (initdb's default for local connections), so it works whatever the superuser's password is. It prints the
    /// names of the roles it reconciled, one per line.
    /// </summary>
    public IReadOnlyList<string> ReconcileCommand =>
        [
            "psql",
            "--no-psqlrc",
            "--quiet",
            "--tuples-only",
            "--no-align",
            "--set",
            "ON_ERROR_STOP=1",
            "--username",
            Superuser,
            "--dbname",
            "postgres",
            "--file",
            "-",
        ];

    /// <summary>
    /// The psql script that sets the superuser's password to <c>POSTGRES_PASSWORD</c> and every existing role's to its
    /// variable, all read from the container's environment by <c>\getenv</c>: the script holds no password, and the
    /// statements are kept out of the server's log and out of <c>pg_stat_statements</c>. A role that does not exist yet
    /// is skipped (the init scripts create it on an empty volume). Idempotent. studioctl's <c>ReconcileScript</c>, byte
    /// for byte.
    /// </summary>
    public string ReconcileScript
    {
        get
        {
            var b = new StringBuilder();
            b.Append("SET log_min_error_statement = panic;\n");
            b.Append("SET log_statement = none;\n");
            b.Append("SET pg_stat_statements.track_utility = off;\n");
            b.Append("\\unset password\n");
            b.Append("\\getenv password " + SuperuserPasswordVariable + "\n");
            b.Append("SELECT format('ALTER ROLE %I PASSWORD %L', current_user, :'password') \\gexec\n");
            var names = new List<string>();
            foreach (var role in Roles)
            {
                names.Add("'" + role.Name + "'");
                b.Append("\\unset password\n");
                b.Append("\\getenv password " + role.Variable + "\n");
                b.Append(
                    "SELECT format('ALTER ROLE %I PASSWORD %L', rolname, :'password') FROM pg_roles WHERE rolname = '"
                        + role.Name
                        + "' \\gexec\n"
                );
            }
            b.Append("\\unset password\n");
            if (names.Count > 0)
            {
                b.Append(
                    "SELECT rolname FROM pg_roles WHERE rolname IN ("
                        + string.Join(", ", names)
                        + ") ORDER BY rolname;\n"
                );
            }
            return b.ToString();
        }
    }

    private static string? ExactPassword(string value)
    {
        var matches = AnyPlaceholder().Matches(value);
        return matches.Count == 1 && matches[0].Value == value ? PasswordRole(matches[0].Groups[1].Value) : null;
    }

    /// <summary>
    /// Refuses a password placeholder whose role is not a plain role name or that the Postgres container does not
    /// carry, a role carried by two variables, and placeholders without a Postgres container whose superuser and its
    /// password are literal: studioctl's <c>checkPasswords</c>.
    /// </summary>
    private void CheckPasswords()
    {
        var uses = new List<(string Container, string Key, string Role)>();
        foreach (var name in _names)
        {
            foreach (var (key, value) in _containers[name].Envs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                foreach (Match match in AnyPlaceholder().Matches(value))
                {
                    if (PasswordRole(match.Groups[1].Value) is { } role)
                    {
                        uses.Add((name, key, role));
                    }
                }
            }
        }
        if (uses.Count == 0)
        {
            return;
        }
        if (uses.FirstOrDefault(u => !RoleName().IsMatch(u.Role)) is { Container: not null } bad)
        {
            throw Invalid(
                $"container {bad.Container}: envs {bad.Key}: bad database password: \"{bad.Role}\" is not a role name"
            );
        }
        if (
            !_containers.TryGetValue(PostgresContainer, out var postgres)
            || !postgres.Envs.TryGetValue(SuperuserVariable, out var user)
            || !postgres.Envs.TryGetValue(SuperuserPasswordVariable, out var password)
            || user.Length == 0
            || password.Length == 0
            || (user + password).Contains("{{", StringComparison.Ordinal)
        )
        {
            throw Invalid(
                $"bad database password: container {PostgresContainer} needs a literal {SuperuserVariable} and {SuperuserPasswordVariable}"
            );
        }
        var carried = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in Roles)
        {
            if (!carried.TryAdd(role.Name, role.Variable))
            {
                throw Invalid(
                    $"bad database password: container {PostgresContainer} carries {role.Name} in both {carried[role.Name]} and {role.Variable}"
                );
            }
        }
        if (uses.FirstOrDefault(u => !carried.ContainsKey(u.Role)) is { Container: not null } orphan)
        {
            throw Invalid(
                $"container {orphan.Container}: envs {orphan.Key}: bad database password: no variable of container {PostgresContainer} is {{{{password:{orphan.Role}}}}}"
            );
        }
    }
}
