package localtestv2

import (
	"errors"
	"maps"
	"slices"
	"strings"
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
		"local.altinn.cloud/notifications":    "http://127.0.0.1:" + hostPortNotifications,
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

func metadataValue(route envtopology.BoundTopologyRoute, key string) string {
	for _, entry := range route.Metadata {
		if entry.Key == key {
			return entry.Value
		}
	}
	return ""
}

func TestAlwaysOnRoutesAreEnabledWithoutAGroup(t *testing.T) {
	t.Parallel()
	for _, route := range boundTopologyConfig(defaultOptions(config.DefaultImages(), t.TempDir())).Routes {
		if route.Match.PathPrefix == "/storage" && (!route.Enabled || metadataValue(route, groupMetadataKey) != "") {
			t.Errorf("storage route = %+v, want enabled without a group", route)
		}
	}
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
	off := 0
	for _, route := range boundTopologyConfig(opts).Routes {
		if route.Match.PathPrefix == "/dialogporten" || route.Match.PathPrefix == "/dialogporten/graphql" {
			off++
			if route.Enabled || metadataValue(route, groupMetadataKey) != groupDialogporten {
				t.Errorf("route %s%s = %+v, want it disabled and naming its group", route.Match.Host, route.Match.PathPrefix, route)
			}
		}
	}
	if off != 2 {
		t.Errorf("%d Dialogporten routes with the group off, want both written disabled for the proxy's 503", off)
	}
	for _, target := range healthTargets(opts) {
		if target.name == componentDialogportenWebApi {
			t.Errorf("health polled for %s with the group off", target.name)
		}
	}
	if groupTable()[groupDialogporten].onByDefault {
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
	if web.Destination.URL != "http://127.0.0.1:"+hostPortDialogportenWeb || !web.Enabled ||
		metadataValue(web, rewriteMetadataKey) != "" || metadataValue(web, groupMetadataKey) != groupDialogporten {
		t.Errorf("WebApi route = %+v, want the WebApi's port, enabled, its group and an unchanged path", web)
	}
	graphql := byPrefix["local.altinn.cloud/dialogporten/graphql"]
	if graphql.Destination.URL != "http://127.0.0.1:"+hostPortDialogportenGQL {
		t.Errorf("GraphQL route = %+v", graphql)
	}
	if metadataValue(graphql, rewriteMetadataKey) != "/graphql" {
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
		wantErr       error
		name          string
		with, without []string
		want          []string
	}{
		{name: "default", want: nil},
		{name: "with", with: []string{"dialogporten"}, want: []string{"dialogporten"}},
		{name: "with receipt", with: []string{"receipt"}, want: []string{"receipt"}},
		{name: "repeated", with: []string{"receipt", "dialogporten"}, want: []string{"dialogporten", "receipt"}},
		{name: "without wins when both are given", with: []string{"dialogporten"}, without: []string{"dialogporten"}},
		{
			name: "with brings in the implied group",
			with: []string{"correspondence"},
			want: []string{"correspondence", "dialogporten"},
		},
		{
			name:    "without removes only the named group",
			with:    []string{"correspondence"},
			without: []string{"correspondence"},
			want:    nil,
		},
		{
			name:    "without an implied group is an error",
			with:    []string{"correspondence"},
			without: []string{"dialogporten"},
			wantErr: errImpliedGroupRemoved,
		},
		{
			name: "everything",
			with: []string{"everything"},
			want: []string{"correspondence", "dialogporten", "everything", "receipt"},
		},
		{
			name:    "everything without one of its groups is an error",
			with:    []string{"everything"},
			without: []string{"receipt"},
			wantErr: errImpliedGroupRemoved,
		},
		{name: "without a group that is off", without: []string{"receipt"}, want: nil},
		{name: "unknown with", with: []string{"nosuch"}, wantErr: errUnknownGroup},
		{name: "unknown without", without: []string{"nosuch"}, wantErr: errUnknownGroup},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()
			got, err := ResolveGroups(tt.with, tt.without)
			if !errors.Is(err, tt.wantErr) || !slices.Equal(got, tt.want) {
				t.Errorf("ResolveGroups(%v, %v) = %v, %v; want %v, error %v", tt.with, tt.without, got, err, tt.want, tt.wantErr)
			}
		})
	}
}

func TestTheImpliedGroupErrorNamesBothGroups(t *testing.T) {
	t.Parallel()
	_, err := ResolveGroups([]string{"correspondence"}, []string{"dialogporten"})
	if err == nil || !strings.Contains(err.Error(), "correspondence implies dialogporten") {
		t.Errorf("error = %v, want it to say that correspondence implies dialogporten", err)
	}
}

func TestEveryGroupIsOffByDefaultAndImpliesOnlyKnownGroups(t *testing.T) {
	t.Parallel()
	table := groupTable()
	for name, g := range table {
		if g.onByDefault {
			t.Errorf("%s is on by default", name)
		}
		for _, implied := range g.implies {
			if _, known := table[implied]; !known || implied == name {
				t.Errorf("%s implies %q", name, implied)
			}
		}
	}
	if got := table[groupEverything].implies; len(got) != len(table)-1 {
		t.Errorf("everything implies %v, want every other group", got)
	}
	if want := []string{"correspondence", "dialogporten", "everything", "receipt"}; !slices.Equal(GroupNames(), want) {
		t.Errorf("GroupNames() = %v, want %v", GroupNames(), want)
	}
}

func TestAGroupWithoutComponentsAddsNothing(t *testing.T) {
	t.Parallel()
	base := defaultOptions(config.DefaultImages(), t.TempDir())
	empty := defaultOptions(config.DefaultImages(), t.TempDir())
	empty.Groups = groupSet([]string{groupEverything})
	if !slices.Equal(containerNamesOf(base), containerNamesOf(empty)) {
		t.Errorf("everything alone changed the components: %v, want %v", containerNamesOf(empty), containerNamesOf(base))
	}
	if len(boundTopologyConfig(base).Routes) != len(boundTopologyConfig(empty).Routes) {
		t.Error("everything alone changed the routes")
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
