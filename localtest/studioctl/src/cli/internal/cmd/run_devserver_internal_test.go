package cmd

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"
)

func TestADevFrontendRunRoutesTheDevServerHostAndRemovesTheRoute(t *testing.T) {
	t.Parallel()
	type request struct{ method, path, contentType, body string }
	var (
		mu       sync.Mutex
		requests []request
	)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, err := io.ReadAll(r.Body)
		if err != nil {
			t.Error(err)
		}
		mu.Lock()
		requests = append(requests, request{r.Method, r.URL.Path, r.Header.Get("Content-Type"), string(body)})
		mu.Unlock()
		if r.Method == http.MethodPut {
			w.WriteHeader(http.StatusOK)
			return
		}
		w.WriteHeader(http.StatusNoContent)
	}))
	defer server.Close()

	ctx := context.Background()
	devServer := frontDoorAppURL(frontendDevServerPort)
	if err := putFrontDoorDevServer(ctx, server.Client(), server.URL+"/", devServer); err != nil {
		t.Fatal(err)
	}
	if err := deleteFrontDoorDevServer(ctx, server.Client(), server.URL); err != nil {
		t.Fatal(err)
	}

	want := []request{
		{
			http.MethodPut, "/localtest/api/routes/frontend-dev-server", "application/json",
			`{"url":"http://host.docker.internal:8080"}`,
		},
		{http.MethodDelete, "/localtest/api/routes/frontend-dev-server", "", ""},
	}
	if len(requests) != len(want) {
		t.Fatalf("requests = %+v, want %+v", requests, want)
	}
	for i := range want {
		if requests[i] != want[i] {
			t.Errorf("request %d = %+v, want %+v", i, requests[i], want[i])
		}
	}
}

func TestARefusedDevServerRouteFailsTheRun(t *testing.T) {
	t.Parallel()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusBadRequest)
	}))
	defer server.Close()
	if err := putFrontDoorDevServer(context.Background(), server.Client(), server.URL, "http://x/path"); err == nil {
		t.Fatal("a 400 from the front door was not an error")
	}
}
