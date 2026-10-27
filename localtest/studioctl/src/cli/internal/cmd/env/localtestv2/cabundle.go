package localtestv2

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"altinn.studio/studioctl/internal/localtestv2/manifest"
	"altinn.studio/studioctl/internal/osutil"
)

// The host's CA bundle: behind a TLS-intercepting proxy (a corporate network, a sandbox) the developer names the
// proxy's CA in STUDIO_CA_BUNDLE, a PEM file. The containers the manifest marks with caBundle get it read-only at
// manifest.CABundlePath, with the variable pointing there; their images import it (pdf3 into its Chromium's NSS
// store, which ignores the system's). The runner mounts a copy under its data directory, not the file itself: the
// copy is readable by the images' users, lies under the home directory that Docker Desktop shares, and can be relabelled
// for SELinux without touching a system file. The copy's name is the bundle's hash, so a changed bundle changes the
// containers' mounts and they are recreated.

// caBundleSumLength is how many hex digits of the bundle's SHA-256 name the copy.
const caBundleSumLength = 16

// hostCABundle is the host's CA bundle as the runner passes it on; the zero value is none.
type hostCABundle struct {
	// Source is the host path STUDIO_CA_BUNDLE names.
	Source string
	// Sum is the SHA-256 of the content, hex.
	Sum     string
	content []byte
}

func noCABundle() hostCABundle {
	return hostCABundle{Source: "", Sum: "", content: nil}
}

// readHostCABundle reads the file STUDIO_CA_BUNDLE names. Unset, or naming no file, is none, without an error; a file
// that cannot be read is none with the error, for the caller to warn about.
func readHostCABundle(getenv func(string) string) (hostCABundle, error) {
	source := getenv(manifest.CABundleVariable)
	if source == "" {
		return noCABundle(), nil
	}
	content, err := os.ReadFile(source) //nolint:gosec // The developer names the file to pass to the containers.
	if errors.Is(err, os.ErrNotExist) {
		return noCABundle(), nil
	}
	if err != nil {
		return noCABundle(), fmt.Errorf("%s=%s: %w", manifest.CABundleVariable, source, err)
	}
	sum := sha256.Sum256(content)
	return hostCABundle{Source: source, Sum: hex.EncodeToString(sum[:]), content: content}, nil
}

// ensureCABundle makes dir hold exactly the bundle's copy, world-readable, and returns its path; with no bundle it
// removes dir and returns "".
func ensureCABundle(dir string, bundle hostCABundle) (string, error) {
	if bundle.Sum == "" {
		if err := os.RemoveAll(dir); err != nil {
			return "", fmt.Errorf("remove the CA bundle's copy: %w", err)
		}
		return "", nil
	}
	if err := os.MkdirAll(dir, osutil.DirPermDefault); err != nil {
		return "", fmt.Errorf("create the CA bundle's directory: %w", err)
	}
	if err := os.Chmod(dir, osutil.DirPermDefault); err != nil {
		return "", fmt.Errorf("make the CA bundle's directory readable: %w", err)
	}
	name := bundle.Sum[:caBundleSumLength] + ".pem"
	entries, err := os.ReadDir(dir)
	if err != nil {
		return "", fmt.Errorf("list the CA bundle's directory: %w", err)
	}
	for _, entry := range entries {
		if entry.Name() != name {
			if removeErr := os.RemoveAll(filepath.Join(dir, entry.Name())); removeErr != nil {
				return "", fmt.Errorf("remove an old CA bundle's copy: %w", removeErr)
			}
		}
	}
	path := filepath.Join(dir, name)
	existing, err := os.ReadFile(path) //nolint:gosec // The path is under studioctl's own data directory.
	if err != nil || !bytes.Equal(existing, bundle.content) {
		if err = os.WriteFile(path, bundle.content, osutil.FilePermDefault); err != nil {
			return "", fmt.Errorf("copy the CA bundle: %w", err)
		}
	}
	// World-readable on purpose, whatever the umask made of it: the containers run as their image users.
	if err := os.Chmod(path, osutil.FilePermDefault); err != nil {
		return "", fmt.Errorf("make the CA bundle's copy readable: %w", err)
	}
	return path, nil
}

// caBundleEnv is what a container with caBundle gets while the host has a bundle: the variable at the mount.
func caBundleEnv(opts *Options, c manifest.Container) map[string]string {
	if !c.CABundle || opts.CABundle == "" {
		return nil
	}
	return map[string]string{manifest.CABundleVariable: manifest.CABundlePath}
}
