package cmd

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"altinn.studio/devenv/pkg/container/mock"
	"altinn.studio/devenv/pkg/container/types"
	appsvc "altinn.studio/studioctl/internal/cmd/app"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

func TestAnAppOnTheHostIsReachedThroughTheHostGateway(t *testing.T) {
	t.Parallel()
	got, err := hostAppURL("http://127.0.0.1:5005")
	if err != nil || got != "http://host.docker.internal:5005" {
		t.Fatalf("hostAppURL = %q, %v", got, err)
	}
	if _, err := hostAppURL("http://127.0.0.1"); err == nil {
		t.Fatal("hostAppURL accepted a URL without a port")
	}
}

// A container-mode app is registered at its alias on the apps network; the run waits for exactly that address.
func TestTheRunWaitsForTheRegistrationAtTheAddressItWasGiven(t *testing.T) {
	t.Parallel()
	cfg := &config.Config{Home: t.TempDir(), DataDir: t.TempDir(), Version: config.NewVersion("test-version")}
	if err := localtestv2.WriteState(cfg, localtestv2.State{CertificateDir: t.TempDir()}); err != nil {
		t.Fatal(err)
	}
	since := time.Now()
	frontDoor := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if _, err := fmt.Fprintf(w,
			`[{"org":"ttd","app":"test-app","url":"http://ttd-test-app:5005",`+
				`"provisioning":{"state":"succeeded","at":%q}}]`,
			since.Add(time.Second).UTC().Format(time.RFC3339Nano),
		); err != nil {
			t.Error(err)
		}
	}))
	t.Cleanup(frontDoor.Close)
	host, port, err := net.SplitHostPort(frontDoor.Listener.Addr().String())
	if err != nil {
		t.Fatal(err)
	}
	topology := envtopology.NewLocalAt(host, port)

	if err := waitForFrontDoorRegistration(
		t.Context(), cfg, topology, "ttd/test-app", "http://ttd-test-app:5005", since, 2*time.Second,
	); err != nil {
		t.Fatalf("at the alias: %v", err)
	}
	if err := waitForFrontDoorRegistration(
		t.Context(), cfg, topology, "ttd/test-app", "http://host.docker.internal:5005", since, time.Second,
	); err == nil {
		t.Fatal("a registration at the alias counted for the host's port")
	}
}

// A container run on the apps network is refused, before any build, when the environment has none (started by an
// earlier studioctl); a run on the localtest runtime's network is not checked.
func TestAContainerRunNeedsTheAppsNetwork(t *testing.T) {
	t.Parallel()
	client := mock.New()
	client.NetworkInspectFunc = func(context.Context, string) (types.NetworkInfo, error) {
		return types.NetworkInfo{}, types.ErrNetworkNotFound
	}
	onApps := appsvc.DockerRunSpec{}
	onApps.Config.Networks = []string{localtestv2.AppsNetwork}
	if err := checkAppsNetwork(t.Context(), client, onApps); !errors.Is(err, errNoAppsNetwork) {
		t.Fatalf("checkAppsNetwork = %v, want errNoAppsNetwork", err)
	}
	if err := checkAppsNetwork(t.Context(), client, appsvc.DockerRunSpec{}); err != nil {
		t.Fatalf("checkAppsNetwork off the apps network = %v", err)
	}
	client.NetworkInspectFunc = func(context.Context, string) (types.NetworkInfo, error) {
		return types.NetworkInfo{Name: localtestv2.AppsNetwork}, nil
	}
	if err := checkAppsNetwork(t.Context(), client, onApps); err != nil {
		t.Fatalf("checkAppsNetwork with the network = %v", err)
	}
}
