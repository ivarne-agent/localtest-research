package localtestv2_test

import (
	"encoding/json"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"testing"

	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

var generated = regexp.MustCompile(`^[0-9a-f]{48}$`)

func readFile(t *testing.T, path string) map[string]string {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var out map[string]string
	if err := json.Unmarshal(data, &out); err != nil {
		t.Fatal(err)
	}
	return out
}

func TestThePasswordFileIsBesideTheState(t *testing.T) {
	t.Parallel()
	cfg := &config.Config{DataDir: "/data"}
	if got := localtestv2.PasswordsPath(cfg); got != filepath.Join("/data", "generated", "localtest-v2", "secrets",
		"postgres-passwords.json") {
		t.Fatalf("path %s", got)
	}
}

func TestAMissingFileGetsARandomPasswordPerRoleOwnerOnly(t *testing.T) {
	t.Parallel()
	path := filepath.Join(t.TempDir(), "generated", "secrets", "postgres-passwords.json")
	got, err := localtestv2.EnsurePasswords(path, []string{"a", "b"})
	if err != nil {
		t.Fatal(err)
	}
	if len(got) != 2 || !generated.MatchString(got["a"]) || !generated.MatchString(got["b"]) || got["a"] == got["b"] {
		t.Fatalf("passwords %d, want two distinct 48-digit hex values", len(got))
	}
	stored := readFile(t, path)
	if stored["a"] != got["a"] || stored["b"] != got["b"] || len(stored) != 2 {
		t.Fatal("the file does not hold what was returned")
	}
	if runtime.GOOS == "windows" {
		return
	}
	file, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	dir, err := os.Stat(filepath.Dir(path))
	if err != nil {
		t.Fatal(err)
	}
	if file.Mode().Perm() != 0o600 || dir.Mode().Perm() != 0o700 {
		t.Fatalf("file %v, directory %v; want 0600 and 0700", file.Mode().Perm(), dir.Mode().Perm())
	}
	entries, err := os.ReadDir(filepath.Dir(path))
	if err != nil || len(entries) != 1 {
		t.Fatalf("the directory holds %v (%v), want the file only: no temporary file left", entries, err)
	}
}

func TestStoredPasswordsAreKeptAndMissingOnesAdded(t *testing.T) {
	t.Parallel()
	path := filepath.Join(t.TempDir(), "secrets", "postgres-passwords.json")
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(
		path,
		[]byte(`{"a": "kept-value-0123456789", "old": "a-role-no-longer-asked"}`),
		0o644,
	); err != nil {
		t.Fatal(err)
	}
	got, err := localtestv2.EnsurePasswords(path, []string{"a", "b"})
	if err != nil {
		t.Fatal(err)
	}
	if got["a"] != "kept-value-0123456789" || !generated.MatchString(got["b"]) || len(got) != 2 {
		t.Fatalf("a %q, b %q, %d roles", got["a"], got["b"], len(got))
	}
	stored := readFile(t, path)
	if stored["old"] != "a-role-no-longer-asked" || stored["b"] != got["b"] {
		t.Fatal("the file lost a role or the new one")
	}
	again, err := localtestv2.EnsurePasswords(path, []string{"a", "b"})
	if err != nil || again["b"] != got["b"] {
		t.Fatalf("a second read changed b (%v)", err)
	}
	if runtime.GOOS == "windows" {
		return
	}
	if file, err := os.Stat(path); err != nil || file.Mode().Perm() != 0o600 {
		t.Fatalf("file mode %v (%v), want 0600", file.Mode().Perm(), err)
	}
	if dir, err := os.Stat(filepath.Dir(path)); err != nil || dir.Mode().Perm() != 0o700 {
		t.Fatalf("directory mode %v (%v), want 0700", dir.Mode().Perm(), err)
	}
}

func TestAnUnusableFileIsRefusedNotOverwritten(t *testing.T) {
	t.Parallel()
	for name, content := range map[string]string{
		"not JSON":              "{",
		"not an object":         `["a"]`,
		"too short":             `{"a": "short"}`,
		"unsafe in a URL":       `{"a": "with@at;semicolon-0123"}`,
		"a quote in the string": `{"a": "it's-0123456789abcdef"}`,
	} {
		path := filepath.Join(t.TempDir(), "postgres-passwords.json")
		if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
			t.Fatal(err)
		}
		if _, err := localtestv2.EnsurePasswords(path, []string{"a"}); err == nil {
			t.Errorf("%s: accepted", name)
		}
		if data, err := os.ReadFile(path); err != nil || string(data) != content {
			t.Errorf("%s: the file changed", name)
		}
	}
}

func TestThePasswordsDigestChangesWithAnyPassword(t *testing.T) {
	t.Parallel()
	one := localtestv2.PasswordsDigest(manifest.Passwords{"a": "x1234567890123456", "b": "y1234567890123456"})
	same := localtestv2.PasswordsDigest(manifest.Passwords{"b": "y1234567890123456", "a": "x1234567890123456"})
	other := localtestv2.PasswordsDigest(manifest.Passwords{"a": "x1234567890123456", "b": "z1234567890123456"})
	if one != same || one == other || !regexp.MustCompile(`^[0-9a-f]{64}$`).MatchString(one) {
		t.Fatalf("digests %s %s %s", one, same, other)
	}
}

func TestNewPasswordsDiffer(t *testing.T) {
	t.Parallel()
	seen := map[string]bool{}
	for range 100 {
		p, err := localtestv2.NewPassword()
		if err != nil {
			t.Fatal(err)
		}
		if !generated.MatchString(p) || seen[p] {
			t.Fatalf("password %d: not 192 bits of fresh hex", len(seen))
		}
		seen[p] = true
	}
}
