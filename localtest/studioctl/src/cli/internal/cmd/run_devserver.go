package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"strings"

	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

// Under localtest-v2 the front door routes app-frontend.local.altinn.cloud, where `--dev-frontend` points the app's
// assets, to the frontend dev server only while one is registered with it (PUT localtest/api/routes/frontend-dev-server;
// without one that host is served like the main host). A `--dev-frontend` run registers the dev server on the host's
// port 8080, where the old runtime's host bridge sent that host too (`yarn docker` in the app frontend builds and serves
// dist there with CORS, `yarn start --port 8080` runs Vite there), and removes the route when the run stops.
const (
	frontDoorDevServerPath = "/localtest/api/routes/frontend-dev-server"
	frontendDevServerPort  = 8080
)

var errFrontDoorDevServer = errors.New("the localtest-v2 front door refused the frontend dev server route")

// frontDoorDevServerRequest is the body of PUT localtest/api/routes/frontend-dev-server.
type frontDoorDevServerRequest struct {
	URL string `json:"url"`
}

// registerDevFrontend routes the frontend dev server host to the dev server for a `--dev-frontend` run against
// localtest-v2, and returns what removes the route again; without the flag or the runtime it does nothing.
func (c *RunCommand) registerDevFrontend(
	ctx context.Context,
	topology envtopology.Local,
	flags runFlags,
) (func(), error) {
	noop := func() {}
	if !flags.devFrontend || c.cfg == nil {
		return noop, nil
	}
	if _, up, err := localtestv2.ReadState(c.cfg); err != nil || !up {
		return noop, err //nolint:wrapcheck // ReadState's error names the state file.
	}
	client := &http.Client{Timeout: frontDoorRequestTimeout}
	localtestURL := topology.LocaltestURL()
	if err := putFrontDoorDevServer(ctx, client, localtestURL, frontDoorAppURL(frontendDevServerPort)); err != nil {
		return noop, err
	}
	c.out.Verbosef(
		"Routed %s to the frontend dev server on port %d",
		topology.FrontendDevServerHostName(),
		frontendDevServerPort,
	)
	return func() {
		cleanupCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), studioctlServerCleanupTimeout)
		defer cancel()
		if err := deleteFrontDoorDevServer(cleanupCtx, client, localtestURL); err != nil {
			c.out.Verbosef("failed to remove the frontend dev server route: %v", err)
		}
	}, nil
}

func putFrontDoorDevServer(ctx context.Context, client *http.Client, localtestURL, devServerURL string) error {
	body, err := json.Marshal(frontDoorDevServerRequest{URL: devServerURL})
	if err != nil {
		return fmt.Errorf("encode the frontend dev server route: %w", err)
	}
	return frontDoorDevServerRequestDo(ctx, client, http.MethodPut, localtestURL, body)
}

func deleteFrontDoorDevServer(ctx context.Context, client *http.Client, localtestURL string) error {
	return frontDoorDevServerRequestDo(ctx, client, http.MethodDelete, localtestURL, nil)
}

func frontDoorDevServerRequestDo(
	ctx context.Context,
	client *http.Client,
	method string,
	localtestURL string,
	body []byte,
) error {
	target := strings.TrimRight(localtestURL, "/") + frontDoorDevServerPath
	req, err := http.NewRequestWithContext(ctx, method, target, bytes.NewReader(body))
	if err != nil {
		return fmt.Errorf("build request: %w", err)
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := client.Do(req)
	if err != nil {
		return fmt.Errorf("%s %s: %w", method, target, err)
	}
	defer func() { ignoreCloseError(resp.Body.Close()) }()
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return fmt.Errorf("%w: %s %s: HTTP %d", errFrontDoorDevServer, method, target, resp.StatusCode)
	}
	return nil
}
