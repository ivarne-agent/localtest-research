package localtestv2

import (
	"maps"
	"slices"
	"testing"

	"altinn.studio/devenv/pkg/resource"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
)

func TestBoundTopologyConfigRoutesEveryComponentToLoopback(t *testing.T) {
	t.Parallel()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	cfg := boundTopologyConfig(opts)

	if cfg.AppRouteTemplate.Host != "local.altinn.cloud" || cfg.AppRouteTemplate.PathPrefixTemplate != "/{org}/{app}" {
		t.Fatalf("app route template = %+v", cfg.AppRouteTemplate)
	}
	byPrefix := map[string]string{}
	for _, route := range cfg.Routes {
		if route.Destination.Location != envtopology.DestinationLocationHost {
			t.Errorf("%s: location %q, want host", route.Component, route.Destination.Location)
		}
		byPrefix[route.Match.Host+route.Match.PathPrefix] = route.Destination.URL
	}
	want := map[string]string{
		"local.altinn.cloud/storage":          "http://127.0.0.1:" + hostPortStorage,
		"local.altinn.cloud/profile":          "http://127.0.0.1:" + hostPortRegister,
		"local.altinn.cloud":                  "http://127.0.0.1:" + hostPortLocaltest,
		"pdf.local.altinn.cloud":              "http://127.0.0.1:" + hostPortPDF,
		"workflow-engine.local.altinn.cloud":  "http://127.0.0.1:" + hostPortWorkflowEngine,
		"app-frontend.local.altinn.cloud":     "http://127.0.0.1:" + frontendDevServerHostPort,
		"local.altinn.cloud/accessmanagement": "http://127.0.0.1:" + hostPortAccessManagement,
		"local.altinn.cloud/events":           "http://127.0.0.1:" + hostPortEvents,
		"local.altinn.cloud/resourceregistry": "http://127.0.0.1:" + hostPortResourceRegistry,
		"local.altinn.cloud/authentication":   "http://127.0.0.1:" + hostPortAuthentication,
		"local.altinn.cloud/authorization":    "http://127.0.0.1:" + hostPortAuthorization,
		"local.altinn.cloud/register":         "http://127.0.0.1:" + hostPortRegister,
	}
	for key, url := range want {
		if byPrefix[key] != url {
			t.Errorf("route %s = %q, want %q", key, byPrefix[key], url)
		}
	}
}

func TestComponentsReachThePlatformThroughTheHostGateway(t *testing.T) {
	t.Parallel()
	for _, c := range components(defaultOptions(config.DefaultImages(), t.TempDir())) {
		if c.name == componentPostgres {
			continue
		}
		for _, host := range []string{"local.altinn.cloud", "app-frontend.local.altinn.cloud", "pdf.local.altinn.cloud"} {
			if !slices.Contains(c.extraHosts, host+":host-gateway") {
				t.Errorf("%s: extra hosts %v lack %s", c.name, c.extraHosts, host)
			}
		}
		if c.hostPort == "" || c.healthPath == "" {
			t.Errorf("%s: needs a host port and a health path for the health wait", c.name)
		}
	}
}

func TestDevComponents(t *testing.T) {
	t.Parallel()
	all, err := devComponents([]string{"all"})
	if err != nil {
		t.Fatal(err)
	}
	if !all[componentStorage] || all[componentPDF] {
		t.Errorf("all = %v: storage is buildable, pdf3 is not", all)
	}
	if _, err := devComponents([]string{"pdf3"}); err == nil {
		t.Error("pdf3 comes from the monorepo and must be rejected")
	}
	if _, err := devComponents([]string{"nosuch"}); err == nil {
		t.Error("unknown component must be rejected")
	}
}

func TestDevComponentImageIsBuiltFromTheResearchCheckout(t *testing.T) {
	t.Parallel()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	opts.DevSourceRoot = "/src/localtest"
	opts.DevComponents = map[string]bool{componentStorage: true, componentPostgres: true}
	for _, c := range components(opts) {
		img := componentImage(opts, c)
		built, isBuilt := img.(*resource.BuiltImage)
		switch c.name {
		case componentStorage:
			if !isBuilt || built.Dockerfile != "/src/localtest/components/storage/Dockerfile" {
				t.Errorf("storage image = %#v", img)
			}
		case componentPostgres:
			if !isBuilt || built.Dockerfile != "/src/localtest/postgres/Dockerfile" ||
				built.ContextPath != "/src/localtest" {
				t.Errorf("postgres image = %#v", img)
			}
		default:
			if isBuilt {
				t.Errorf("%s: built although not selected", c.name)
			}
		}
	}
}

func dialogportenOptions(t *testing.T) *Options {
	t.Helper()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	opts.Groups = groupSet([]string{groupDialogporten})
	return opts
}

func containerNamesOf(opts *Options) []string {
	var names []string
	for _, c := range components(opts) {
		names = append(names, c.name)
	}
	return names
}

func TestTheDialogportenGroupIsOffByDefault(t *testing.T) {
	t.Parallel()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	for _, name := range containerNamesOf(opts) {
		if slices.Contains(
			[]string{componentRedis, componentDialogportenWebApi, componentDialogportenGraphQL, componentDialogportenService},
			name,
		) {
			t.Errorf("%s is part of the default environment", name)
		}
	}
	for _, route := range boundTopologyConfig(opts).Routes {
		if route.Match.PathPrefix == "/dialogporten" || route.Match.PathPrefix == "/dialogporten/graphql" {
			t.Errorf("route %s%s registered with the group off", route.Match.Host, route.Match.PathPrefix)
		}
	}
	for _, target := range healthTargets(opts) {
		if target.name == componentDialogportenWebApi {
			t.Errorf("health polled for %s with the group off", target.name)
		}
	}
	if dialogportenOnByDefault {
		t.Error("the group is opt-in")
	}
}

func TestTheDialogportenGroupRegistersItsRoutesAndHealthWhenOn(t *testing.T) {
	t.Parallel()
	opts := dialogportenOptions(t)
	if got := containerNamesOf(opts); !slices.Contains(got, componentRedis) ||
		!slices.Contains(got, componentDialogportenService) {
		t.Fatalf("components = %v, want the group's four", got)
	}

	byPrefix := map[string]envtopology.BoundTopologyRoute{}
	for _, route := range boundTopologyConfig(opts).Routes {
		byPrefix[route.Match.Host+route.Match.PathPrefix] = route
	}
	web := byPrefix["local.altinn.cloud/dialogporten"]
	if web.Destination.URL != "http://127.0.0.1:"+hostPortDialogportenWeb || len(web.Metadata) != 0 {
		t.Errorf("WebApi route = %+v, want the WebApi's port and an unchanged path", web)
	}
	graphql := byPrefix["local.altinn.cloud/dialogporten/graphql"]
	if graphql.Destination.URL != "http://127.0.0.1:"+hostPortDialogportenGQL {
		t.Errorf("GraphQL route = %+v", graphql)
	}
	if len(graphql.Metadata) != 1 || graphql.Metadata[0].Key != "rewritePrefix" || graphql.Metadata[0].Value != "/graphql" {
		t.Errorf("GraphQL route metadata = %+v, want rewritePrefix /graphql", graphql.Metadata)
	}
	if len(byPrefix) != len(boundTopologyConfig(opts).Routes) {
		t.Error("two routes share a host and prefix")
	}

	health := map[string]string{}
	for _, target := range healthTargets(opts) {
		health[target.name] = target.url
	}
	for name, port := range map[string]string{
		componentDialogportenWebApi:  hostPortDialogportenWeb,
		componentDialogportenGraphQL: hostPortDialogportenGQL,
		componentDialogportenService: hostPortDialogportenSvc,
	} {
		if want := "http://127.0.0.1:" + port + "/health/readiness"; health[name] != want {
			t.Errorf("health of %s = %q, want %q", name, health[name], want)
		}
	}
	if _, polled := health[componentRedis]; polled {
		t.Error("redis has no published port and is covered by its container health check")
	}
}

func TestTheDialogportenHostsStartAfterTheWebApiThatMigrates(t *testing.T) {
	t.Parallel()
	for _, c := range components(dialogportenOptions(t)) {
		switch c.name {
		case componentDialogportenGraphQL, componentDialogportenService:
			if !slices.Contains(c.deps, containerOf(componentDialogportenWebApi)) {
				t.Errorf("%s deps %v lack the WebApi, which migrates the database", c.name, c.deps)
			}
		case componentDialogportenWebApi:
			for _, dep := range []string{componentPostgres, componentRedis, componentResourceRegistry, componentEvents} {
				if !slices.Contains(c.deps, containerOf(dep)) {
					t.Errorf("WebApi deps %v lack %s", c.deps, dep)
				}
			}
		}
	}
}

func TestResolveGroups(t *testing.T) {
	t.Parallel()
	tests := []struct {
		name          string
		with, without []string
		want          []string
		wantErr       bool
	}{
		{name: "default", want: nil},
		{name: "with", with: []string{"dialogporten"}, want: []string{"dialogporten"}},
		{name: "without wins when both are given", with: []string{"dialogporten"}, without: []string{"dialogporten"}},
		{name: "unknown with", with: []string{"nosuch"}, wantErr: true},
		{name: "unknown without", without: []string{"nosuch"}, wantErr: true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()
			got, err := ResolveGroups(tt.with, tt.without)
			if (err != nil) != tt.wantErr || !slices.Equal(got, tt.want) {
				t.Errorf("ResolveGroups(%v, %v) = %v, %v; want %v, error %v", tt.with, tt.without, got, err, tt.want, tt.wantErr)
			}
		})
	}
}

func TestTeardownCoversEveryGroup(t *testing.T) {
	t.Parallel()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	opts.Groups = allGroups()
	var names []string
	for _, res := range resources(opts) {
		if ctr, ok := res.(*resource.Container); ok {
			names = append(names, ctr.Name)
		}
	}
	for _, want := range []string{"localtest-v2-redis", "localtest-v2-dialogporten-webapi", "localtest-v2-dialogporten-service"} {
		if !slices.Contains(names, want) {
			t.Errorf("teardown graph lacks %s", want)
		}
	}
}

func TestDialogportenIsBuiltAsOneDevComponentFromThreeDockerfiles(t *testing.T) {
	t.Parallel()
	selected, err := devComponents([]string{"dialogporten"})
	if err != nil || !selected["dialogporten"] {
		t.Fatalf("--dev dialogporten = %v, %v", selected, err)
	}
	if _, err := devComponents([]string{componentDialogportenWebApi}); err == nil {
		t.Error("the hosts are built together as the group, not one by one")
	}
	opts := dialogportenOptions(t)
	opts.DevSourceRoot = "/src/localtest"
	opts.DevComponents = selected
	dockerfiles := map[string]string{}
	for _, c := range components(opts) {
		if built, ok := componentImage(opts, c).(*resource.BuiltImage); ok {
			dockerfiles[c.name] = built.Dockerfile
			if built.ContextPath != "/src/localtest/components/dialogporten" {
				t.Errorf("%s context = %s", c.name, built.ContextPath)
			}
		}
	}
	want := map[string]string{
		componentDialogportenWebApi:  "/src/localtest/components/dialogporten/Dockerfile.webapi",
		componentDialogportenGraphQL: "/src/localtest/components/dialogporten/Dockerfile.graphql",
		componentDialogportenService: "/src/localtest/components/dialogporten/Dockerfile.service",
	}
	if !maps.Equal(dockerfiles, want) {
		t.Errorf("built images = %v, want %v", dockerfiles, want)
	}
}
