package manifest

import (
	"errors"
	"slices"
	"strings"
	"testing"
)

func TestTheRolesAreThePostgresVariablesThatCarryAPassword(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	want := []Role{
		{Name: "arbeidsflate", Variable: "ARBEIDSFLATE_PASSWORD"},
		{Name: "correspondence", Variable: "CORRESPONDENCE_PASSWORD"},
		{Name: "dialogporten", Variable: "DIALOGPORTEN_PASSWORD"},
		{Name: "platform_authorization", Variable: "PLATFORM_AUTHORIZATION_PASSWORD"},
		{Name: "platform_authorization_admin", Variable: "PLATFORM_AUTHORIZATION_ADMIN_PASSWORD"},
		{Name: "platform_events", Variable: "PLATFORM_EVENTS_PASSWORD"},
		{Name: "platform_events_admin", Variable: "PLATFORM_EVENTS_ADMIN_PASSWORD"},
		{Name: "platform_notifications", Variable: "PLATFORM_NOTIFICATIONS_PASSWORD"},
		{Name: "platform_notifications_admin", Variable: "PLATFORM_NOTIFICATIONS_ADMIN_PASSWORD"},
		{Name: "platform_profile", Variable: "PLATFORM_PROFILE_PASSWORD"},
		{Name: "platform_profile_admin", Variable: "PLATFORM_PROFILE_ADMIN_PASSWORD"},
		{Name: "platform_storage", Variable: "PLATFORM_STORAGE_PASSWORD"},
		{Name: "platform_storage_admin", Variable: "PLATFORM_STORAGE_ADMIN_PASSWORD"},
	}
	if got := m.Roles(); !slices.Equal(got, want) {
		t.Fatalf("roles %v, want %v", got, want)
	}
	if m.Superuser() != "postgres" || m.Containers[PostgresContainer].Envs[SuperuserPasswordVariable] != "Password" {
		t.Fatalf("superuser %q with %q, want postgres with the fixed Password", m.Superuser(),
			m.Containers[PostgresContainer].Envs[SuperuserPasswordVariable])
	}
}

// testToolPrefix names Access Management's settings for the test tool's token generator, which the front door mocks.
const testToolPrefix = "Platform__Token__TestTool__"

// No password in the manifest: every value that carries one names it by its placeholder.
func TestTheManifestHoldsNoRolePassword(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	for _, name := range m.Names() {
		for key, value := range m.Containers[name].Envs {
			if key == SuperuserPasswordVariable || strings.Contains(value, "Username=postgres;") {
				continue // the superuser's fixed password, the recovery path
			}
			if strings.HasPrefix(key, testToolPrefix) {
				continue // the front door's test-tool mock accepts any basic auth: inert values, not a role password
			}
			lower := strings.ToLower(key + "=" + value)
			if (strings.Contains(lower, "password=") || strings.HasSuffix(key, "Pwd") ||
				strings.HasSuffix(key, "_PASSWORD")) && !strings.Contains(value, "{{password:") &&
				!strings.Contains(value, "Password={0}") {
				t.Errorf("%s: %s=%s holds a password", name, key, value)
			}
		}
	}
}

func TestEveryPasswordPlaceholderResolvesWithTheInstallationsPasswords(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	passwords := Passwords{}
	for _, role := range m.RoleNames() {
		passwords[role] = "pw-" + role
	}
	envs := m.Envs("storage", m.DefaultOrigin(), passwords)
	if envs["PostgreSqlSettings__StorageDbAdminPwd"] != "pw-platform_storage_admin" {
		t.Errorf("storage admin password %q", envs["PostgreSqlSettings__StorageDbAdminPwd"])
	}
	dialogporten := m.Envs("dialogporten-webapi", m.DefaultOrigin(), passwords)["Infrastructure__DialogDbConnectionString"]
	if !strings.Contains(dialogporten, ";Password=pw-dialogporten;") {
		t.Errorf("dialogporten connection string %q", dialogporten)
	}
	postgres := m.Envs(PostgresContainer, m.DefaultOrigin(), passwords)
	for _, r := range m.Roles() {
		if postgres[r.Variable] != "pw-"+r.Name {
			t.Errorf("postgres %s=%q", r.Variable, postgres[r.Variable])
		}
	}
	// A role the passwords lack stays a placeholder, which the runner prevents by filling every role first.
	if got := (Passwords{}).Resolve("a{{password:x}}b{{origin}}"); got != "a{{password:x}}b{{origin}}" {
		t.Errorf("Resolve without the role = %q", got)
	}
}

func TestABadPasswordPlaceholderIsAManifestError(t *testing.T) {
	t.Parallel()
	cases := map[string]struct {
		mutate func(envs, postgres map[string]any)
	}{
		"a role postgres does not carry": {func(envs, _ map[string]any) {
			envs["PostgreSqlSettings__StorageDbPwd"] = "{{password:nobody}}"
		}},
		"not a role name": {func(envs, _ map[string]any) {
			envs["PostgreSqlSettings__StorageDbPwd"] = "{{password:Platform-Storage}}"
		}},
		"a role carried twice": {func(_, postgres map[string]any) {
			postgres["SECOND_PASSWORD"] = "{{password:platform_storage}}"
		}},
		"no superuser password": {func(_, postgres map[string]any) {
			delete(postgres, SuperuserPasswordVariable)
		}},
		"a generated superuser password": {func(_, postgres map[string]any) {
			postgres[SuperuserPasswordVariable] = "{{password:postgres}}"
		}},
	}
	for name, tc := range cases {
		err := mutated(t, func(m map[string]any) {
			envs, ok := containerNamed(m, "storage")["envs"].(map[string]any)
			if !ok {
				t.Fatal("storage has no envs")
			}
			postgres, ok := containerNamed(m, PostgresContainer)["envs"].(map[string]any)
			if !ok {
				t.Fatal("postgres has no envs")
			}
			tc.mutate(envs, postgres)
		})
		if !errors.Is(err, errPassword) || !errors.Is(err, errInvalid) {
			t.Errorf("%s: %v, want a password error", name, err)
		}
	}
}

func TestTheReconcileScriptReadsEveryPasswordFromTheContainersEnvironment(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	script := m.ReconcileScript()
	lines := strings.Split(strings.TrimSuffix(script, "\n"), "\n")
	for _, first := range []string{
		"SET log_min_error_statement = panic;", "SET log_statement = none;",
		"SET pg_stat_statements.track_utility = off;",
	} {
		if !slices.Contains(lines[:3], first) {
			t.Errorf("the script does not start with %q", first)
		}
	}
	if !strings.Contains(script, "\\getenv password POSTGRES_PASSWORD\n"+
		"SELECT format('ALTER ROLE %I PASSWORD %L', current_user, :'password') \\gexec\n") {
		t.Error("the superuser's password is not set from POSTGRES_PASSWORD")
	}
	for _, r := range m.Roles() {
		want := "\\unset password\n\\getenv password " + r.Variable + "\n" +
			"SELECT format('ALTER ROLE %I PASSWORD %L', rolname, :'password') FROM pg_roles WHERE rolname = '" +
			r.Name + "' \\gexec\n"
		if !strings.Contains(script, want) {
			t.Errorf("%s is not set from %s only when it exists", r.Name, r.Variable)
		}
	}
	if strings.Contains(script, "Password'") || strings.Contains(script, "{{") {
		t.Error("the script holds a value")
	}
	command := m.ReconcileCommand()
	if command[0] != "psql" || !slices.Contains(command, "ON_ERROR_STOP=1") || slices.Contains(command, "--host") ||
		command[slices.Index(command, "--username")+1] != "postgres" {
		t.Errorf("command %v: psql as the superuser over the Unix socket, stopping at the first error", command)
	}
}
