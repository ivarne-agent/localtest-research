package localtestv2

import (
	"bytes"
	"io"
	"slices"
	"strings"
	"testing"

	"altinn.studio/devenv/pkg/container/types"
	"altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/ui"
)

// The front door is the one container on both networks: the stack's and the apps network, where it reaches a
// container-mode app by its alias.
func TestTheFrontDoorJoinsTheAppsNetwork(t *testing.T) {
	t.Parallel()
	f, client := newFakeRuntime()
	cfg := testConfig(t)
	env := NewEnv(cfg, ui.NewOutput(io.Discard, io.Discard, false), client)
	if err := env.applyResources(t.Context(), resources(defaultOptions(localtestv2.CertificateDir(cfg)), nil), false); err != nil {
		t.Fatal(err)
	}
	apps, ok := f.networks[localtestv2.AppsNetwork]
	if !ok || apps.Labels[localtestv2.Label] != localtestv2.LabelAppsNetwork {
		t.Fatalf("apps network %+v (created: %v)", apps, ok)
	}
	for name, container := range f.containers {
		want := []string{networkName}
		if name == containerOf(frontDoor) {
			want = append(want, localtestv2.AppsNetwork)
		}
		if !slices.Equal(container.Networks, want) {
			t.Errorf("%s is on %v, want %v", name, container.Networks, want)
		}
	}
}

// env down removes an app container left on the apps network, which the network's removal would fail on, and then the
// network itself.
func TestDownRemovesTheAppContainersAndTheAppsNetwork(t *testing.T) {
	t.Parallel()
	f, client := newFakeRuntime()
	cfg := testConfig(t)
	writeTestState(t, cfg, false)
	var out bytes.Buffer
	env := NewEnv(cfg, ui.NewOutput(&out, io.Discard, false), client)
	if err := env.applyResources(t.Context(), resources(defaultOptions(localtestv2.CertificateDir(cfg)), nil), false); err != nil {
		t.Fatal(err)
	}
	const app = "localtest-app-test-app"
	f.containers[app] = types.ContainerConfig{
		Name:     app,
		Networks: []string{localtestv2.AppsNetwork},
		Labels:   map[string]string{localtestv2.Label: localtestv2.LabelAppContainer},
	}
	if err := env.Down(t.Context()); err != nil {
		t.Fatalf("Down: %v\n%s", err, out.String())
	}
	if left := f.left(); len(left) != 0 {
		t.Errorf("left after env down: %v", left)
	}
	if !strings.Contains(out.String(), "Removed app container "+app) {
		t.Errorf("output does not name the app container:\n%s", out.String())
	}
}
