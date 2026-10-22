package cmd

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"

	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

// The localtest-v2 front door (the localtest container) routes /{org}/{app}/ to an app once the app is registered with
// it, and provisions the app from its API on registration. studioctl-server registers every app it discovers, a run's
// and one started from an IDE alike (PUT localtest/api/apps/{org}/{app} with the address below); a run waits until the
// front door lists the app at that address with a provisioning that succeeded since the run started.
const (
	frontDoorAppsPath       = "/localtest/api/apps"
	frontDoorPollInterval   = 500 * time.Millisecond
	frontDoorRequestTimeout = 5 * time.Second
	frontDoorProvisioned    = "succeeded"
	// frontDoorClockSlack allows for the container's clock against the host's when comparing registration times.
	frontDoorClockSlack = time.Second
)

var errFrontDoorRegistration = errors.New("the localtest-v2 front door has not registered the app")

// frontDoorAppURL is an app's address as the front door reaches it: the host's port through the runtime's host gateway.
func frontDoorAppURL(port int) string {
	return "http://host.docker.internal:" + strconv.Itoa(port)
}

// frontDoorRoute is one entry of GET localtest/api/apps.
type frontDoorRoute struct {
	Provisioning *struct {
		At    time.Time `json:"at"`
		State string    `json:"state"`
		Error string    `json:"error"`
	} `json:"provisioning"`
	Org string `json:"org"`
	App string `json:"app"`
	URL string `json:"url"`
}

// waitForFrontDoorRegistration waits, while the localtest-v2 runtime is up, until the front door lists the app at the
// address of baseURL's port with a provisioning that succeeded at or after since. Without the runtime it returns at once.
func waitForFrontDoorRegistration(
	ctx context.Context,
	cfg *config.Config,
	topology envtopology.Local,
	appID string,
	baseURL string,
	since time.Time,
	timeout time.Duration,
) error {
	if _, up, err := localtestv2.ReadState(cfg); err != nil || !up {
		return err //nolint:wrapcheck // ReadState's error names the state file.
	}
	parsed, err := url.Parse(baseURL)
	if err != nil {
		return fmt.Errorf("parse app URL %q: %w", baseURL, err)
	}
	port, err := strconv.Atoi(parsed.Port())
	if err != nil {
		return fmt.Errorf("app URL %q has no port: %w", baseURL, err)
	}
	org, app, _ := strings.Cut(appID, "/")
	want := frontDoorAppURL(port)
	appsURL := strings.TrimRight(topology.LocaltestURL(), "/") + frontDoorAppsPath

	waitCtx, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	client := &http.Client{Timeout: frontDoorRequestTimeout}
	var last string
	for {
		routes, err := frontDoorRoutes(waitCtx, client, appsURL)
		if err != nil {
			last = err.Error()
		} else {
			done, state := registered(routes, org, app, want, since.Add(-frontDoorClockSlack))
			if done {
				return nil
			}
			last = state
		}
		select {
		case <-waitCtx.Done():
			if ctx.Err() != nil {
				return fmt.Errorf("wait for the front door: %w", ctx.Err())
			}
			return fmt.Errorf("%w %s at %s within %s: %s", errFrontDoorRegistration, appID, want, timeout, last)
		case <-time.After(frontDoorPollInterval):
		}
	}
}

// registered reports whether the routes have the app at want, provisioned since; otherwise it describes what they have.
func registered(routes []frontDoorRoute, org, app, want string, since time.Time) (bool, string) {
	for _, route := range routes {
		if route.Org != org || route.App != app {
			continue
		}
		if route.URL != want {
			return false, "registered at " + route.URL
		}
		p := route.Provisioning
		switch {
		case p == nil:
			return false, "registered, provisioning"
		case p.At.Before(since):
			return false, "registered, provisioned before this run"
		case p.State != frontDoorProvisioned:
			return false, "provisioning " + p.State + ": " + p.Error
		}
		return true, ""
	}
	return false, "not registered"
}

func frontDoorRoutes(ctx context.Context, client *http.Client, appsURL string) ([]frontDoorRoute, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, appsURL, nil)
	if err != nil {
		return nil, fmt.Errorf("build request: %w", err)
	}
	resp, err := client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("GET %s: %w", appsURL, err)
	}
	defer func() { ignoreCloseError(resp.Body.Close()) }()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("%w: GET %s: HTTP %d", errFrontDoorRegistration, appsURL, resp.StatusCode)
	}
	var routes []frontDoorRoute
	if err := json.NewDecoder(resp.Body).Decode(&routes); err != nil {
		return nil, fmt.Errorf("decode %s: %w", appsURL, err)
	}
	return routes, nil
}

func ignoreCloseError(error) {}
