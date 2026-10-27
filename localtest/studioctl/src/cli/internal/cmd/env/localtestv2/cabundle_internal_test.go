package localtestv2

import (
	"bytes"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"

	"altinn.studio/devenv/pkg/container"
	"altinn.studio/devenv/pkg/container/types"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
	"altinn.studio/studioctl/internal/ui"
)

const (
	// testCABundleContent is the CA bundle of the golden file's everything case; the Aspire package's parity test
	// copies the same content, so its copy has the same name.
	testCABundleContent = "localtest-v2 test CA bundle\n"
	// testCABundle is the runner's copy of testCABundleContent: its SHA-256's first 16 hex digits.
	testCABundle = "/home/dev/.altinn-studio/ca-bundle/8901b546104a875e.pem"
)

// newTestEnv is NewEnv on a host without STUDIO_CA_BUNDLE, whatever the machine running the tests has.
func newTestEnv(cfg *config.Config, out *ui.Output, client container.ContainerClient) *Env {
	return withHostEnv(NewEnv(cfg, out, client), map[string]string{})
}

func withHostEnv(e *Env, vars map[string]string) *Env {
	e.getenv = func(key string) string { return vars[key] }
	return e
}

func writeBundle(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "corporate.pem")
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
	return path
}

func bundleOf(t *testing.T, content string) hostCABundle {
	t.Helper()
	source := writeBundle(t, content)
	bundle, err := readHostCABundle(func(string) string { return source })
	if err != nil {
		t.Fatal(err)
	}
	return bundle
}

func TestTheGoldenCABundleIsNamedByItsContent(t *testing.T) {
	t.Parallel()
	if got := bundleOf(t, testCABundleContent).Sum[:caBundleSumLength] + ".pem"; got != filepath.Base(testCABundle) {
		t.Errorf("the copy of the golden bundle is %s, the golden file says %s", got, testCABundle)
	}
}

func TestTheHostCABundleIsReadWhenTheVariableNamesAFile(t *testing.T) {
	t.Parallel()
	source := writeBundle(t, "-----BEGIN CERTIFICATE-----\n")
	got, err := readHostCABundle(func(string) string { return source })
	if err != nil || got.Source != source || len(got.Sum) != 64 {
		t.Fatalf("got %+v, %v", got, err)
	}
	for name, value := range map[string]string{
		"unset":        "",
		"no such file": filepath.Join(t.TempDir(), "missing.pem"),
	} {
		none, noneErr := readHostCABundle(func(string) string { return value })
		if noneErr != nil || none.Sum != "" {
			t.Errorf("%s: got %+v, %v; want none without an error", name, none, noneErr)
		}
	}
	// A directory exists but cannot be read as a file, on every platform.
	dir := t.TempDir()
	got, err = readHostCABundle(func(string) string { return dir })
	if err == nil || got.Sum != "" || !strings.Contains(err.Error(), manifest.CABundleVariable+"="+dir) {
		t.Errorf("unreadable: got %+v, %v; want none with an error naming the variable", got, err)
	}
}

func TestAnUnreadableCABundleWarnsOnceAndStartsWithout(t *testing.T) {
	t.Parallel()
	var stderr bytes.Buffer
	out := ui.NewOutput(&bytes.Buffer{}, &stderr, false)
	env := withHostEnv(NewEnv(testConfig(t), out, nil), map[string]string{manifest.CABundleVariable: t.TempDir()})
	for range 3 {
		if got := env.caBundle(); got.Sum != "" {
			t.Fatalf("got %+v", got)
		}
	}
	if n := strings.Count(stderr.String(), manifest.CABundleVariable); n != 1 {
		t.Errorf("%d warnings: %q", n, stderr.String())
	}
}

func TestTheCABundleIsCopiedReadableUnderItsHash(t *testing.T) {
	t.Parallel()
	dir := filepath.Join(t.TempDir(), "ca-bundle")
	first := bundleOf(t, "first")
	path, err := ensureCABundle(dir, first)
	if err != nil {
		t.Fatal(err)
	}
	if want := filepath.Join(dir, first.Sum[:caBundleSumLength]+".pem"); path != want {
		t.Errorf("path %s, want %s", path, want)
	}
	if data, readErr := os.ReadFile(path); readErr != nil || string(data) != "first" {
		t.Errorf("content %q, %v", data, readErr)
	}
	if info, statErr := os.Stat(path); statErr != nil || info.Mode().Perm()&0o044 != 0o044 {
		t.Errorf("not readable by everyone: %v, %v", info, statErr)
	}

	other, err := ensureCABundle(dir, bundleOf(t, "second"))
	if err != nil {
		t.Fatal(err)
	}
	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	if other == path || len(entries) != 1 || entries[0].Name() != filepath.Base(other) {
		t.Errorf("a changed bundle: %s, entries %v", other, entries)
	}

	none, err := ensureCABundle(dir, hostCABundle{})
	if err != nil || none != "" {
		t.Fatalf("none: %q, %v", none, err)
	}
	if _, err := os.Stat(dir); !os.IsNotExist(err) {
		t.Errorf("the copy is left without a bundle: %v", err)
	}
}

func TestOnlyTheContainersWithCABundleGetIt(t *testing.T) {
	t.Parallel()
	with := defaultOptions(testCertDir, testFilesDir)
	with.Groups = OptionalGroups()
	with.CABundle = testCABundle
	without := defaultOptions(testCertDir, testFilesDir)
	without.Groups = OptionalGroups()

	var marked []string
	for name, c := range containersOf(t, with) {
		mounted := slices.ContainsFunc(c.Volumes, func(v types.VolumeMount) bool {
			return v.ContainerPath == manifest.CABundlePath && v.HostPath == testCABundle && v.ReadOnly
		})
		variable := envOf(c)[manifest.CABundleVariable]
		if stack().Containers[name].CABundle {
			marked = append(marked, name)
			if !mounted || variable != manifest.CABundlePath {
				t.Errorf("%s: mounted %v, %s=%q", name, mounted, manifest.CABundleVariable, variable)
			}
		} else if mounted || variable != "" {
			t.Errorf("%s has the bundle without caBundle", name)
		}
	}
	if !slices.Equal(marked, []string{"pdf3"}) {
		t.Errorf("caBundle containers %v, want pdf3", marked)
	}
	for name, c := range containersOf(t, without) {
		if _, set := envOf(c)[manifest.CABundleVariable]; set {
			t.Errorf("%s has %s without a bundle", name, manifest.CABundleVariable)
		}
		for _, v := range c.Volumes {
			if v.ContainerPath == manifest.CABundlePath {
				t.Errorf("%s mounts the bundle without one", name)
			}
		}
	}
}

func TestAnUpWithAnotherCABundleChangesTheRunningEnvironment(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	bundle := bundleOf(t, "bundle")
	source := bundle.Source
	write := func(sum string) {
		t.Helper()
		if err := localtestv2.WriteState(cfg, localtestv2.State{
			CertificateDir: localtestv2.CertificateDir(cfg),
			CABundle:       sum,
		}); err != nil {
			t.Fatal(err)
		}
	}
	withBundle := withHostEnv(NewEnv(cfg, nil, nil), map[string]string{manifest.CABundleVariable: source})
	write("")
	if changed, err := newTestEnv(cfg, nil, nil).Changed(UpOptions{}); err != nil || changed {
		t.Errorf("none, none: %v, %v", changed, err)
	}
	if changed, err := withBundle.Changed(UpOptions{}); err != nil || !changed {
		t.Errorf("none, then a bundle: %v, %v", changed, err)
	}
	write(bundle.Sum)
	if changed, err := withBundle.Changed(UpOptions{}); err != nil || changed {
		t.Errorf("the same bundle: %v, %v", changed, err)
	}
	if changed, err := newTestEnv(cfg, nil, nil).Changed(UpOptions{}); err != nil || !changed {
		t.Errorf("a bundle, then none: %v, %v", changed, err)
	}
	write("another")
	if changed, err := withBundle.Changed(UpOptions{}); err != nil || !changed {
		t.Errorf("another bundle: %v, %v", changed, err)
	}
}
