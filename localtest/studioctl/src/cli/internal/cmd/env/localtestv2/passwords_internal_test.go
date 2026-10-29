package localtestv2

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"slices"
	"strings"
	"testing"

	"altinn.studio/devenv/pkg/container/mock"
	"altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
	"altinn.studio/studioctl/internal/ui"
)

var errExec = errors.New("exit status 3")

func TestThePasswordsAreReconciledAfterPostgresIsHealthyAndBeforeItsDependants(t *testing.T) {
	t.Parallel()
	var order []string
	probes := func(target healthTarget) func(ctx context.Context) error {
		return func(context.Context) error {
			order = append(order, "probe "+target.name)
			if target.name == "storage" {
				return errUnhealthy
			}
			return nil
		}
	}
	wrapped := reconcilingAfter(probes, func(context.Context) error {
		order = append(order, "reconcile")
		return nil
	})
	for _, name := range []string{manifest.PostgresContainer, "redis", "storage"} {
		if err := wrapped(healthTargetOf(name))(t.Context()); (err != nil) != (name == "storage") {
			t.Errorf("%s: %v", name, err)
		}
	}
	want := []string{"probe postgres", "reconcile", "probe redis", "probe storage"}
	if !slices.Equal(order, want) {
		t.Fatalf("order %v, want %v", order, want)
	}

	order = nil
	failing := reconcilingAfter(func(healthTarget) func(ctx context.Context) error {
		return func(context.Context) error { return errUnhealthy }
	}, func(context.Context) error {
		order = append(order, "reconcile")
		return nil
	})
	if err := failing(healthTargetOf(manifest.PostgresContainer))(t.Context()); !errors.Is(err, errUnhealthy) ||
		len(order) != 0 {
		t.Fatalf("an unhealthy Postgres: %v, reconciled %v", err, order)
	}
}

func TestTheReconciliationRunsTheScriptOnStdinInThePostgresContainer(t *testing.T) {
	t.Parallel()
	client := mock.New()
	var stdin string
	client.ExecWithIOFunc = func(_ context.Context, _ string, _ []string, in io.Reader, out, _ io.Writer) error {
		data, err := io.ReadAll(in)
		if err != nil {
			return fmt.Errorf("read stdin: %w", err)
		}
		stdin = string(data)
		if _, err := io.WriteString(out, "platform_storage\nplatform_storage_admin\n"); err != nil {
			return fmt.Errorf("write stdout: %w", err)
		}
		return nil
	}
	var verbose bytes.Buffer
	env := newTestEnv(testConfig(t), ui.NewOutput(&verbose, &verbose, true), client)
	opts := defaultOptions(testCertDir, testFilesDir)
	opts.Passwords = goldenPasswords()
	if err := env.reconcilePasswords(opts)(t.Context()); err != nil {
		t.Fatal(err)
	}
	if len(client.Calls) != 1 {
		t.Fatalf("calls %v", client.Calls)
	}
	command, ok := client.Calls[0].Args[1].([]string)
	if client.Calls[0].Method != "ExecWithIO" || client.Calls[0].Args[0] != "localtest-v2-postgres" || !ok ||
		!slices.Equal(command, stack().ReconcileCommand()) {
		t.Fatalf("calls %v", client.Calls)
	}
	if stdin != stack().ReconcileScript() {
		t.Fatal("the script is not on stdin")
	}
	for _, password := range opts.Passwords {
		if strings.Contains(stdin, password) || strings.Contains(verbose.String(), password) {
			t.Fatal("a password in the script or the output")
		}
	}
	if !strings.Contains(verbose.String(), "2 roles (platform_storage, platform_storage_admin)") {
		t.Errorf("verbose output %q", verbose.String())
	}
}

func TestAFailedReconciliationMasksThePasswords(t *testing.T) {
	t.Parallel()
	client := mock.New()
	opts := defaultOptions(testCertDir, testFilesDir)
	opts.Passwords = goldenPasswords()
	client.ExecWithIOFunc = func(_ context.Context, _ string, _ []string, _ io.Reader, _, stderr io.Writer) error {
		if _, err := io.WriteString(stderr, "ERROR: near '"+opts.Passwords["platform_storage"]+"'\n"); err != nil {
			return fmt.Errorf("write stderr: %w", err)
		}
		return errExec
	}
	err := newTestEnv(testConfig(t), nil, client).reconcilePasswords(opts)(t.Context())
	if err == nil || strings.Contains(err.Error(), opts.Passwords["platform_storage"]) ||
		!strings.Contains(err.Error(), "near '***'") {
		t.Fatalf("error %v", err)
	}
}

// A lost or edited password file is a change: env up on a running environment converges it to the new passwords.
func TestOtherPasswordsChangeTheRunningEnvironment(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	if err := localtestv2.WriteState(cfg, localtestv2.State{
		Passwords:      goldenPasswordsDigest(),
		CertificateDir: localtestv2.CertificateDir(cfg),
	}); err != nil {
		t.Fatal(err)
	}
	if changed, err := newTestEnv(cfg, nil, nil).Changed(UpOptions{}); err != nil || changed {
		t.Fatalf("the same passwords: %v, %v", changed, err)
	}
	other := newTestEnv(cfg, nil, nil)
	other.passwords = func() (manifest.Passwords, error) {
		p := goldenPasswords()
		p["platform_storage"] = "another-0123456789"
		return p, nil
	}
	if changed, err := other.Changed(UpOptions{}); err != nil || !changed {
		t.Fatalf("other passwords: %v, %v", changed, err)
	}
}

// The runner's own passwords come from the file under its data directory, completed with every role.
func TestTheRunnerReadsAndCompletesItsPasswordFile(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	env := NewEnv(cfg, nil, nil)
	passwords, err := env.passwords()
	if err != nil {
		t.Fatal(err)
	}
	if len(passwords) != len(stack().Roles()) {
		t.Fatalf("%d passwords, want one per role", len(passwords))
	}
	if _, statErr := os.Stat(localtestv2.PasswordsPath(cfg)); statErr != nil {
		t.Fatal(statErr)
	}
	again, err := NewEnv(cfg, nil, nil).passwords()
	if err != nil || localtestv2.PasswordsDigest(again) != localtestv2.PasswordsDigest(passwords) {
		t.Fatalf("a second command read other passwords (%v)", err)
	}
}
