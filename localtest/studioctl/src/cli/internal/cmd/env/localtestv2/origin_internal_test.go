package localtestv2

import (
	"context"
	"errors"
	"strings"
	"testing"

	"altinn.studio/devenv/pkg/container/mock"
	envtypes "altinn.studio/studioctl/internal/cmd/env"
	"altinn.studio/studioctl/internal/localtestv2"
)

var errNoSuchHost = errors.New("no such host")

func TestTheRequestedOriginKeepsTheRunningOneUnlessTheFlagsSayOtherwise(t *testing.T) {
	t.Parallel()
	running := localtestv2.State{CertificateDir: "/c", PublicHost: "origin.test", PublicPort: 18000}
	type testCase struct {
		name     string
		wantHost string
		opts     envtypes.UpOptions
		state    localtestv2.State
		wantPort int
		up       bool
		changed  bool
	}
	tc := func(
		name string, opts envtypes.UpOptions, state localtestv2.State, up bool, host string, port int, changed bool,
	) testCase {
		return testCase{
			name: name, opts: opts, state: state, up: up, wantHost: host, wantPort: port, changed: changed,
		}
	}
	cases := []testCase{
		tc("no state, no flags", envtypes.UpOptions{}, localtestv2.State{}, false, "local.altinn.cloud", 8000, false),
		tc("no state, --port 0 picks", envtypes.UpOptions{PublicPort: new(0)}, localtestv2.State{}, false,
			"local.altinn.cloud", 0, false),
		tc("no state, both flags", envtypes.UpOptions{PublicHost: "altinn.test", PublicPort: new(18001)},
			localtestv2.State{}, false, "altinn.test", 18001, false),
		tc("running, no flags keeps it", envtypes.UpOptions{}, running, true, "origin.test", 18000, false),
		tc("running, --port 0 keeps its port", envtypes.UpOptions{PublicPort: new(0)}, running, true,
			"origin.test", 18000, false),
		tc("running, the same values", envtypes.UpOptions{PublicHost: "origin.test", PublicPort: new(18000)}, running,
			true, "origin.test", 18000, false),
		tc(
			"running, another port",
			envtypes.UpOptions{PublicPort: new(8000)},
			running,
			true,
			"origin.test",
			8000,
			true,
		),
		tc("running, another host", envtypes.UpOptions{PublicHost: "local.altinn.cloud"}, running, true,
			"local.altinn.cloud", 18000, true),
		tc("running at the default, a host", envtypes.UpOptions{PublicHost: "origin.test"},
			localtestv2.State{CertificateDir: "/c"}, true, "origin.test", 8000, true),
	}
	for _, c := range cases {
		host, p := requestedOrigin(c.opts, c.state, c.up)
		if host != c.wantHost || p != c.wantPort {
			t.Errorf("%s: %s:%d, want %s:%d", c.name, host, p, c.wantHost, c.wantPort)
		}
		if got := originChanged(c.opts, c.state, c.up); got != c.changed {
			t.Errorf("%s: changed %v, want %v", c.name, got, c.changed)
		}
	}
}

func TestAnUpAtAnotherOriginChangesTheRunningEnvironment(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	writeTestState(t, cfg, false)
	env := NewEnv(cfg, nil, nil)
	if changed, err := env.Changed(envtypes.UpOptions{}); err != nil || changed {
		t.Errorf("the same origin: %v, %v", changed, err)
	}
	if changed, err := env.Changed(envtypes.UpOptions{PublicPort: new(18000)}); err != nil || !changed {
		t.Errorf("another port: %v, %v", changed, err)
	}
	if changed, err := env.Changed(envtypes.UpOptions{PublicPort: new(0)}); err != nil || changed {
		t.Errorf("--port 0 on a running environment: %v, %v", changed, err)
	}
}

func TestAnOriginSwitchStopsTheRunningEnvironment(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	writeTestState(t, cfg, false)
	client := mock.New()
	if err := NewEnv(cfg, nil, client).stopOnSwitch(t.Context(), defaultOptions("")); err != nil {
		t.Fatal(err)
	}
	if len(client.Calls) != 0 {
		t.Fatalf("the same origin looked at the containers: %v", client.Calls)
	}
	custom := defaultOptions("")
	custom.Origin = stack().Origin("origin.test", 18000)
	if err := NewEnv(cfg, nil, client).stopOnSwitch(t.Context(), custom); err != nil {
		t.Fatal(err)
	}
	if len(client.Calls) == 0 {
		t.Fatal("an origin switch did not look for the running containers")
	}
}

func TestTheOriginFlagsAreChecked(t *testing.T) {
	t.Parallel()
	for _, opts := range []envtypes.UpOptions{
		{PublicHost: "Origin.Test"},
		{PublicHost: "origin.test:18000"},
		{PublicHost: "127.0.0.1"},
		{PublicPort: new(-1)},
		{PublicPort: new(70000)},
	} {
		if err := checkOriginFlags(opts); !errors.Is(err, errOriginFlag) {
			t.Errorf("%+v: %v", opts, err)
		}
	}
	for _, opts := range []envtypes.UpOptions{{}, {PublicHost: "origin.test", PublicPort: new(0)}, {PublicPort: new(1)}} {
		if err := checkOriginFlags(opts); err != nil {
			t.Errorf("%+v: %v", opts, err)
		}
	}
}

func TestACustomHostMustResolveToThisMachine(t *testing.T) {
	t.Parallel()
	resolved := map[string][]string{
		"origin.test":     {"127.0.0.1", "::1"},
		"pdf.origin.test": {"127.0.0.1"},
		"af.origin.test":  {"192.168.1.10"},
	}
	lookup := func(_ context.Context, host string) ([]string, error) {
		if addresses, ok := resolved[host]; ok {
			return addresses, nil
		}
		return nil, errNoSuchHost
	}
	err := checkResolution(t.Context(), lookup, stack().Origin("origin.test", 18000))
	if !errors.Is(err, errUnresolved) {
		t.Fatalf("%v, want an unresolved error", err)
	}
	const line = "\n127.0.0.1 workflow-engine.origin.test app-frontend.origin.test af.origin.test"
	if !strings.HasSuffix(err.Error(), line) {
		t.Errorf("%q does not end with the hosts-file line %q", err, line)
	}
	for _, host := range []string{"workflow-engine.origin.test", "app-frontend.origin.test", "af.origin.test"} {
		resolved[host] = []string{"127.0.0.1"}
	}
	if err := checkResolution(t.Context(), lookup, stack().Origin("origin.test", 18000)); err != nil {
		t.Errorf("every name resolves: %v", err)
	}
	if err := checkResolution(t.Context(), lookup, stack().DefaultOrigin()); err != nil {
		t.Errorf("the default host is not checked here: %v", err)
	}
}

func TestPortZeroPicksAFreePortAndTheStateRecordsIt(t *testing.T) {
	t.Parallel()
	cfg := testConfig(t)
	env := NewEnv(cfg, nil, mock.New())
	opts, err := env.options(t.Context(), envtypes.UpOptions{PublicHost: "origin.test", PublicPort: new(0)})
	if err != nil {
		t.Fatal(err)
	}
	if opts.Origin.Host != "origin.test" || opts.Origin.Port <= 0 || opts.Origin.Port == 8000 {
		t.Fatalf("origin %s", opts.Origin.URL())
	}
	if writeErr := localtestv2.WriteState(cfg, localtestv2.State{
		CertificateDir: opts.CertDir, PublicHost: opts.Origin.Host, PublicPort: opts.Origin.Port,
	}); writeErr != nil {
		t.Fatal(writeErr)
	}
	current, err := env.currentOptions()
	if err != nil {
		t.Fatal(err)
	}
	if current.Origin.URL() != opts.Origin.URL() {
		t.Errorf("status and down see %s, up started %s", current.Origin.URL(), opts.Origin.URL())
	}
	again, err := env.options(t.Context(), envtypes.UpOptions{PublicPort: new(0)})
	if err != nil || again.Origin.URL() != opts.Origin.URL() {
		t.Errorf("a second --port 0 moved the origin to %s (%v)", again.Origin.URL(), err)
	}
}
