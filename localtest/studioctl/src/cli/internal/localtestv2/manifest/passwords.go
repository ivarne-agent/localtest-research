package manifest

import (
	"errors"
	"fmt"
	"maps"
	"regexp"
	"slices"
	"strings"
)

// Database passwords. The manifest holds no role password: a value names one by the placeholder {{password:<role>}},
// which the runner resolves from its own file of passwords generated for the installation (each runner keeps one in its
// data directory). The Postgres container's envs say which variable carries each role's password: the variable whose
// value is exactly the role's placeholder, which the image's init scripts read to create the role on an empty volume.
// Every other value that names a role's placeholder carries the same password in the form its service reads (a password
// setting, a connection string). The superuser is the container's POSTGRES_USER with the fixed POSTGRES_PASSWORD, both
// literal: it is the recovery path. After Postgres is healthy and before anything that depends on it starts, the runner
// runs ReconcileScript in the container, which sets every existing role's password (and the superuser's) to the values
// of the container's environment, so a lost password file or an older volume never needs a reset.
const (
	// PostgresContainer is the stack's Postgres: its envs carry the role passwords, and the runner reconciles them in
	// it.
	PostgresContainer = "postgres"
	// SuperuserVariable names the superuser, in the Postgres container's envs.
	SuperuserVariable = "POSTGRES_USER"
	// SuperuserPasswordVariable holds the superuser's fixed password, in the Postgres container's envs.
	SuperuserPasswordVariable = "POSTGRES_PASSWORD" //nolint:gosec // A variable's name, not a password.

	placeholderPassword = "password"
)

var (
	errPassword = errors.New("bad database password")

	// roleName is a role a placeholder may name: a lower-case SQL identifier that needs no quoting.
	roleName = regexp.MustCompile(`^[a-z_][a-z0-9_]{0,62}$`)
)

// Role is one database role whose password the runner generates: its name, and the variable of the Postgres container
// that carries the password.
type Role struct {
	Name     string
	Variable string
}

// Passwords are the role passwords of one installation, by role name.
type Passwords map[string]string

// Resolve replaces every {{password:<role>}} in value with the role's password. A role without one stays as it is,
// which the runner prevents by filling every role of the manifest first.
func (p Passwords) Resolve(value string) string {
	return placeholder.ReplaceAllStringFunc(value, func(match string) string {
		role, ok := passwordRole(match[2 : len(match)-2])
		if !ok {
			return match
		}
		if password, ok := p[role]; ok {
			return password
		}
		return match
	})
}

// passwordRole is the role of a password placeholder's inner text (password:<role>), and whether it is one.
func passwordRole(inner string) (string, bool) {
	name, role, ok := strings.Cut(inner, ":")
	if !ok || name != placeholderPassword {
		return "", false
	}
	return role, true
}

// Roles are the roles whose passwords the runner generates, sorted by name: one per variable of the Postgres container
// whose value is exactly a role's placeholder.
func (m *Manifest) Roles() []Role {
	envs := m.Containers[PostgresContainer].Envs
	var roles []Role
	for _, key := range slices.Sorted(maps.Keys(envs)) {
		if role, ok := exactPassword(envs[key]); ok {
			roles = append(roles, Role{Name: role, Variable: key})
		}
	}
	slices.SortFunc(roles, func(a, b Role) int { return strings.Compare(a.Name, b.Name) })
	return roles
}

// RoleNames are the names of Roles, sorted.
func (m *Manifest) RoleNames() []string {
	roles := m.Roles()
	names := make([]string, 0, len(roles))
	for _, r := range roles {
		names = append(names, r.Name)
	}
	return names
}

// Superuser is the Postgres superuser's name.
func (m *Manifest) Superuser() string {
	return m.Containers[PostgresContainer].Envs[SuperuserVariable]
}

func exactPassword(value string) (string, bool) {
	if !strings.HasPrefix(value, placeholderOpen) || !strings.HasSuffix(value, "}}") {
		return "", false
	}
	matches := placeholder.FindAllStringSubmatch(value, -1)
	if len(matches) != 1 || matches[0][0] != value {
		return "", false
	}
	return passwordRole(matches[0][1])
}

// passwordUse is one password placeholder in the manifest: the container, the envs key and the role.
type passwordUse struct {
	container string
	key       string
	role      string
}

// passwordUses are the manifest's password placeholders, in file order of the containers and sorted keys.
func (m *Manifest) passwordUses() []passwordUse {
	var uses []passwordUse
	for _, name := range m.names {
		envs := m.Containers[name].Envs
		for _, key := range slices.Sorted(maps.Keys(envs)) {
			for _, match := range placeholder.FindAllStringSubmatch(envs[key], -1) {
				if role, ok := passwordRole(match[1]); ok {
					uses = append(uses, passwordUse{container: name, key: key, role: role})
				}
			}
		}
	}
	return uses
}

// checkPasswords refuses a password placeholder whose role is not a plain role name, or that the Postgres container
// does not carry (it would never be created or reconciled), a role carried by two variables, and a manifest with
// placeholders but no Postgres container with a superuser and its literal password.
func (m *Manifest) checkPasswords() error {
	uses := m.passwordUses()
	if len(uses) == 0 {
		return nil
	}
	for _, u := range uses {
		if !roleName.MatchString(u.role) {
			return fmt.Errorf("container %s: envs %s: %w: %q is not a role name (%s)",
				u.container, u.key, errPassword, u.role, roleName)
		}
	}
	if err := m.checkSuperuser(); err != nil {
		return err
	}
	carried := map[string]string{}
	for _, r := range m.Roles() {
		if other, ok := carried[r.Name]; ok {
			return fmt.Errorf("%w: container %s carries %s in both %s and %s", errPassword, PostgresContainer,
				r.Name, other, r.Variable)
		}
		carried[r.Name] = r.Variable
	}
	for _, u := range uses {
		if carried[u.role] == "" {
			return fmt.Errorf("container %s: envs %s: %w: no variable of container %s is {{password:%s}}",
				u.container, u.key, errPassword, PostgresContainer, u.role)
		}
	}
	return nil
}

// checkSuperuser refuses a manifest without a Postgres container whose superuser and its password are literal.
func (m *Manifest) checkSuperuser() error {
	postgres, ok := m.Containers[PostgresContainer]
	if !ok {
		return fmt.Errorf("%w: password placeholders without a %s container", errPassword, PostgresContainer)
	}
	user, password := postgres.Envs[SuperuserVariable], postgres.Envs[SuperuserPasswordVariable]
	if user == "" || password == "" || strings.Contains(user+password, placeholderOpen) {
		return fmt.Errorf("%w: container %s needs a literal %s and %s", errPassword, PostgresContainer,
			SuperuserVariable, SuperuserPasswordVariable)
	}
	return nil
}

// ReconcileCommand is the command the runner executes in the Postgres container, with ReconcileScript on its standard
// input: psql as the superuser over the container's Unix socket, which the image's pg_hba.conf trusts (initdb's
// default for local connections), so it works whatever the superuser's password is. It prints the names of the roles it
// reconciled, one per line.
func (m *Manifest) ReconcileCommand() []string {
	return []string{
		"psql", "--no-psqlrc", "--quiet", "--tuples-only", "--no-align",
		"--set", "ON_ERROR_STOP=1", "--username", m.Superuser(), "--dbname", "postgres", "--file", "-",
	}
}

// ReconcileScript is the psql script that sets the superuser's password to POSTGRES_PASSWORD and every existing role's
// to its variable, all read from the container's environment by \getenv: the script itself holds no password, and the
// statements are kept out of the server's log (log_min_error_statement, log_statement) and out of pg_stat_statements.
// A role that does not exist yet is skipped; the init scripts create it on an empty volume. Idempotent.
func (m *Manifest) ReconcileScript() string {
	var b strings.Builder
	b.WriteString("SET log_min_error_statement = panic;\n")
	b.WriteString("SET log_statement = none;\n")
	b.WriteString("SET pg_stat_statements.track_utility = off;\n")
	b.WriteString("\\unset password\n")
	b.WriteString("\\getenv password " + SuperuserPasswordVariable + "\n")
	b.WriteString("SELECT format('ALTER ROLE %I PASSWORD %L', current_user, :'password') \\gexec\n")
	roles := m.Roles()
	names := make([]string, 0, len(roles))
	for _, r := range roles {
		names = append(names, "'"+r.Name+"'")
		b.WriteString("\\unset password\n")
		b.WriteString("\\getenv password " + r.Variable + "\n")
		b.WriteString(
			"SELECT format('ALTER ROLE %I PASSWORD %L', rolname, :'password') FROM pg_roles WHERE rolname = '" +
				r.Name + "' \\gexec\n",
		)
	}
	b.WriteString("\\unset password\n")
	if len(names) > 0 {
		b.WriteString("SELECT rolname FROM pg_roles WHERE rolname IN (" + strings.Join(names, ", ") +
			") ORDER BY rolname;\n")
	}
	return b.String()
}
