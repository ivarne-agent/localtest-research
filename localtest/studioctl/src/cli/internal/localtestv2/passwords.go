package localtestv2

import (
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"maps"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"slices"

	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
	"altinn.studio/studioctl/internal/osutil"
)

// The database passwords of this installation: one random password per role of the manifest (manifest.Roles), kept in
// a JSON object of role name to password, readable by the owner only, beside the state file. A role without one gets a
// new one, saved before anything starts; an existing one is never changed. The Aspire package keeps its own file
// alike under its data directory (PostgresPasswords.cs), with the same format.
const (
	passwordsDirName  = "secrets"
	passwordsFileName = "postgres-passwords.json"

	// passwordBytes is the entropy of a generated password: 192 bits, written as 48 lower-case hex digits, which need
	// no quoting in a connection string, a URL, a shell or psql.
	passwordBytes = 24
)

var (
	errPasswordFile = errors.New("bad database password file")

	// storedPassword is a password the file may hold: what is safe in every form the services read it in (connection
	// strings, a postgres:// URL) without quoting, at least as long as 64 bits of hex.
	storedPassword = regexp.MustCompile(`^[A-Za-z0-9_-]{16,256}$`)
)

// PasswordsPath is the file of the installation's database passwords.
func PasswordsPath(cfg *config.Config) string {
	return filepath.Join(cfg.DataDir, stateDirName, passwordsDirName, passwordsFileName)
}

// EnsurePasswords reads the password file at path and returns a password for every role: the stored one, or a new one,
// which it saves (atomically, owner-only, in an owner-only directory) before returning. Roles the file holds that are
// not asked for are kept.
func EnsurePasswords(path string, roles []string) (manifest.Passwords, error) {
	stored, err := readPasswords(path)
	if err != nil {
		return nil, err
	}
	changed := false
	for _, role := range roles {
		if _, ok := stored[role]; ok {
			continue
		}
		password, err := NewPassword()
		if err != nil {
			return nil, err
		}
		stored[role] = password
		changed = true
	}
	if changed {
		if err := writePasswords(path, stored); err != nil {
			return nil, err
		}
	} else if err := restrictFile(path); err != nil {
		return nil, err
	}
	out := make(manifest.Passwords, len(roles))
	for _, role := range roles {
		out[role] = stored[role]
	}
	return out, nil
}

// NewPassword is a random password: passwordBytes from the system's random source, in lower-case hex.
func NewPassword() (string, error) {
	b := make([]byte, passwordBytes)
	if _, err := rand.Read(b); err != nil {
		return "", fmt.Errorf("generate a database password: %w", err)
	}
	return hex.EncodeToString(b), nil
}

// PasswordsDigest identifies the passwords of the roles (hex SHA-256 over the sorted role=password lines), so a change
// of the file is seen as a change of the environment without recording any password.
func PasswordsDigest(passwords manifest.Passwords) string {
	h := sha256.New()
	for _, role := range slices.Sorted(maps.Keys(passwords)) {
		h.Write([]byte(role + "=" + passwords[role] + "\n"))
	}
	return hex.EncodeToString(h.Sum(nil))
}

func readPasswords(path string) (map[string]string, error) {
	data, err := os.ReadFile(path) //nolint:gosec // The path is under the configured studioctl home.
	if errors.Is(err, os.ErrNotExist) {
		return map[string]string{}, nil
	}
	if err != nil {
		return nil, fmt.Errorf("read the database passwords: %w", err)
	}
	stored := map[string]string{}
	if err := json.Unmarshal(data, &stored); err != nil {
		return nil, fmt.Errorf("%w %s: %w (delete it to generate new passwords; the next env up sets them)",
			errPasswordFile, path, err)
	}
	for _, role := range slices.Sorted(maps.Keys(stored)) {
		if !storedPassword.MatchString(stored[role]) {
			return nil, fmt.Errorf("%w %s: the password of %s is not 16 to 256 letters, digits, - or _ "+
				"(delete the entry to generate a new one)", errPasswordFile, path, role)
		}
	}
	return stored, nil
}

func writePasswords(path string, passwords map[string]string) error {
	if err := ensureOwnerOnlyDir(filepath.Dir(path)); err != nil {
		return err
	}
	data, err := json.MarshalIndent(passwords, "", "  ")
	if err != nil {
		return fmt.Errorf("encode the database passwords: %w", err)
	}
	if err := osutil.WriteFileAtomic(path, append(data, '\n'),
		osutil.AtomicWriteOptions{Perm: osutil.FilePermOwnerOnly, OwnerOnly: true}); err != nil {
		return fmt.Errorf("store the database passwords: %w", err)
	}
	return nil
}

// ensureOwnerOnlyDir creates the directory owner-only, and restricts it when it exists with more.
func ensureOwnerOnlyDir(dir string) error {
	if err := os.MkdirAll(dir, osutil.DirPermOwnerOnly); err != nil {
		return fmt.Errorf("create %s: %w", dir, err)
	}
	if runtime.GOOS == osutil.OSWindows {
		return nil
	}
	if err := os.Chmod(dir, osutil.DirPermOwnerOnly); err != nil {
		return fmt.Errorf("restrict %s to its owner: %w", dir, err)
	}
	return nil
}

// restrictFile makes an existing password file and its directory owner-only again, in case they were loosened.
func restrictFile(path string) error {
	if runtime.GOOS == osutil.OSWindows {
		return nil
	}
	if err := ensureOwnerOnlyDir(filepath.Dir(path)); err != nil {
		return err
	}
	if err := os.Chmod(path, osutil.FilePermOwnerOnly); err != nil && !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("restrict %s to its owner: %w", path, err)
	}
	return nil
}
