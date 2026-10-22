package appsecrets_test

import (
	"bytes"
	"os"
	"path/filepath"
	"testing"

	"altinn.studio/studioctl/internal/appsecrets"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

// appCodesGoldenPath holds the bytes WriteDevelopmentAppCodes writes. The Aspire package
// (localtest/aspire/Altinn.App.Aspire) writes the same file for an app it runs and compares its bytes with this one.
//
//nolint:gochecknoglobals // A test fixture path.
var appCodesGoldenPath = filepath.Join("testdata", "app-codes.golden.json")

func TestTheDevelopmentAppCodesMatchTheGoldenFile(t *testing.T) {
	t.Parallel()
	dir := filepath.Join(t.TempDir(), "secrets")
	if err := appsecrets.WriteDevelopmentAppCodes(dir); err != nil {
		t.Fatal(err)
	}
	got, err := os.ReadFile(appsecrets.AppCodesPath(dir))
	if err != nil {
		t.Fatal(err)
	}
	if os.Getenv("STUDIOCTL_UPDATE_GOLDEN") != "" {
		if writeErr := os.WriteFile(appCodesGoldenPath, got, 0o600); writeErr != nil {
			t.Fatal(writeErr)
		}
		return
	}
	want, err := os.ReadFile(appCodesGoldenPath)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(got, want) {
		t.Errorf("the app codes differ from %s:\n%s", appCodesGoldenPath, got)
	}
}

// TestTheManifestNamesTheSecretsAsStudioctlDoes keeps the manifest's app section (what other runners read) and the
// names the rest of studioctl uses (doctor, `app maskinporten set`) together.
func TestTheManifestNamesTheSecretsAsStudioctlDoes(t *testing.T) {
	t.Parallel()
	secrets := manifest.MustDefault().App.Secrets
	if secrets.DirectoryEnv != appsecrets.EnvSecretsDir {
		t.Errorf("directoryEnv = %q, want %q", secrets.DirectoryEnv, appsecrets.EnvSecretsDir)
	}
	want := map[string][2]string{
		"maskinporten": {appsecrets.EnvMaskinportenFileName, appsecrets.MaskinportenFileName},
		"appCodes":     {appsecrets.EnvAppCodesFileName, appsecrets.AppCodesFileName},
	}
	if len(secrets.Files) != len(want) {
		t.Errorf("files = %v, want %v", secrets.Files, want)
	}
	for name, w := range want {
		f, ok := secrets.SecretFile(name)
		if !ok || f.Env != w[0] || f.FileName != w[1] {
			t.Errorf("file %s = %+v, want %v", name, f, w)
		}
	}
	if f, _ := secrets.SecretFile("appCodes"); !f.WrittenOnEveryRun {
		t.Error("the app codes are written on every run")
	}
}
