package localtestv2

import (
	"bytes"
	"context"
	"fmt"
	"strings"

	"altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

// Database passwords. The manifest names each role's password by a placeholder; the installation's values come from the
// password file (localtestv2.EnsurePasswords), which gets a new random password for every role it lacks before
// anything starts. Postgres's init scripts create the roles with them on an empty volume. On a volume that is older than
// the file (or after the file was lost), the roles have other passwords: so once Postgres is healthy, and before
// anything that depends on it starts, the runner sets every existing role's password, and the superuser's fixed one, to
// the container's environment (manifest.ReconcileScript, run by psql over the container's Unix socket). The passwords
// are in the container's environment only: never in an argument, a log line or the script.

// loadPasswords reads the password file, completing it with a password for every role of the manifest.
func (e *Env) loadPasswords() (manifest.Passwords, error) {
	passwords, err := localtestv2.EnsurePasswords(localtestv2.PasswordsPath(e.cfg), stack().RoleNames())
	if err != nil {
		return nil, fmt.Errorf("database passwords: %w", err)
	}
	return passwords, nil
}

// reconcilingAfter runs reconcile after the Postgres container's readiness check passed, as part of it, so that what
// depends on Postgres starts only once the passwords are set.
func reconcilingAfter(probes readinessProbes, reconcile func(ctx context.Context) error) readinessProbes {
	return func(target healthTarget) func(ctx context.Context) error {
		probe := probes(target)
		if target.name != manifest.PostgresContainer {
			return probe
		}
		return func(ctx context.Context) error {
			if err := probe(ctx); err != nil {
				return err
			}
			return reconcile(ctx)
		}
	}
}

// reconcilePasswords runs the reconciliation script in the Postgres container: its passwords come from the container's
// environment, which the options' passwords set.
func (e *Env) reconcilePasswords(opts *Options) func(ctx context.Context) error {
	return func(ctx context.Context) error {
		var stdout, stderr bytes.Buffer
		err := e.client.ExecWithIO(ctx, containerOf(manifest.PostgresContainer), stack().ReconcileCommand(),
			strings.NewReader(stack().ReconcileScript()), &stdout, &stderr)
		if err != nil {
			return fmt.Errorf("set the database passwords in %s: %w: %s", containerOf(manifest.PostgresContainer),
				err, masked(strings.TrimSpace(stderr.String()), opts.Passwords))
		}
		roles := strings.Fields(stdout.String())
		e.out.Verbosef("database passwords set: the superuser and %d roles (%s)", len(roles), strings.Join(roles, ", "))
		return nil
	}
}

// masked is text with every password replaced, for an error message.
func masked(text string, passwords manifest.Passwords) string {
	for _, password := range passwords {
		if password != "" {
			text = strings.ReplaceAll(text, password, "***")
		}
	}
	return text
}
