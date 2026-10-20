package localtestv2

import (
	"errors"
	"maps"
	"slices"
	"strings"
	"testing"

	"altinn.studio/devenv/pkg/container/types"
	"altinn.studio/devenv/pkg/resource"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
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
			[]string{
				componentRedis,
				componentDialogportenWebApi,
				componentDialogportenGraphQL,
				componentDialogportenService,
				componentDialogportenAdapter,
			},
			name,
		) {
			t.Errorf("%s is part of the default environment", name)
		}
	}
	off := 0
	for _, route := range boundTopologyConfig(opts).Routes {
		groupPrefixes := []string{"/dialogporten", "/dialogporten/graphql", "/storage/dialogporten"}
		if slices.Contains(groupPrefixes, route.Match.PathPrefix) {
			off++
			if route.Enabled || metadataValue(route, groupMetadataKey) != groupDialogporten {
				t.Errorf("route %s%s = %+v, want it disabled and naming its group", route.Match.Host, route.Match.PathPrefix, route)
			}
		}
	}
	if off != 3 {
		t.Errorf("%d Dialogporten routes with the group off, want all three written disabled for the proxy's 503", off)
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
		t.Fatalf("components = %v, want the group's five", got)
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

func TestTheDialogportenAdapterRoutesStorageDialogportenAndIsPolled(t *testing.T) {
	t.Parallel()
	opts := dialogportenOptions(t)
	byPrefix := map[string]envtopology.BoundTopologyRoute{}
	for _, route := range boundTopologyConfig(opts).Routes {
		byPrefix[route.Match.Host+route.Match.PathPrefix] = route
	}
	adapter := byPrefix["local.altinn.cloud/storage/dialogporten"]
	if adapter.Component != componentDialogportenAdapter ||
		adapter.Destination.URL != "http://127.0.0.1:"+hostPortDialogportenAdpt || !adapter.Enabled ||
		metadataValue(adapter, rewriteMetadataKey) != "" {
		t.Errorf("adapter route = %+v, want the adapter's port and an unchanged path", adapter)
	}
	// The proxy picks the longest matching prefix, so the adapter's paths do not reach Storage.
	if storage := byPrefix["local.altinn.cloud/storage"]; storage.Component != componentStorage {
		t.Errorf("/storage route = %+v, want Storage beside the adapter's longer prefix", storage)
	}
	for _, target := range healthTargets(opts) {
		if target.name == componentDialogportenAdapter {
			if want := "http://127.0.0.1:" + hostPortDialogportenAdpt + "/health"; target.url != want {
				t.Errorf("health of the adapter = %q, want %q", target.url, want)
			}
			return
		}
	}
	t.Error("the adapter's health is not polled")
}

func TestTheDialogportenAdapterIsItsOwnDevComponent(t *testing.T) {
	t.Parallel()
	selected, err := devComponents([]string{componentDialogportenAdapter})
	if err != nil || !selected[componentDialogportenAdapter] {
		t.Fatalf("--dev %s = %v, %v", componentDialogportenAdapter, selected, err)
	}
	opts := dialogportenOptions(t)
	opts.DevSourceRoot = "/src/localtest"
	opts.DevComponents = selected
	var built []string
	for _, c := range components(opts) {
		if image, ok := componentImage(opts, c).(*resource.BuiltImage); ok {
			built = append(built, c.name)
			if image.Dockerfile != "/src/localtest/components/dialogporten-adapter/Dockerfile" {
				t.Errorf("adapter Dockerfile = %s", image.Dockerfile)
			}
		}
	}
	if !slices.Equal(built, []string{componentDialogportenAdapter}) {
		t.Errorf("built = %v, want only the adapter", built)
	}
	for _, c := range components(opts) {
		if c.name == componentDialogportenAdapter && c.group != groupDialogporten {
			t.Errorf("adapter group = %q, want %q", c.group, groupDialogporten)
		}
	}
}

func receiptOf(t *testing.T, opts *Options) component {
	t.Helper()
	for _, c := range components(opts) {
		if c.name == componentReceipt {
			return c
		}
	}
	t.Fatal("no receipt container")
	return component{}
}

func receiptOptions(t *testing.T) *Options {
	t.Helper()
	opts := defaultOptions(config.DefaultImages(), "/certs-dir")
	opts.Groups = groupSet([]string{groupReceipt})
	return opts
}

func TestTheReceiptIsInItsOwnGroup(t *testing.T) {
	t.Parallel()
	if slices.Contains(containerNamesOf(defaultOptions(config.DefaultImages(), t.TempDir())), componentReceipt) {
		t.Error("the receipt is part of the default environment")
	}
	receipt := receiptOf(t, receiptOptions(t))
	if receipt.containerPort != "5060" || receipt.hostPort != hostPortReceipt || receipt.healthPath != healthPath {
		t.Errorf("receipt ports and health = %s %s %s", receipt.containerPort, receipt.hostPort, receipt.healthPath)
	}
	for _, dep := range []string{componentRegister, componentAuthentication, componentStorage} {
		if !slices.Contains(receipt.deps, containerOf(dep)) {
			t.Errorf("receipt deps %v lack %s", receipt.deps, dep)
		}
	}
}

func TestTheReceiptSignsPlatformTokensWithTheSharedCertificate(t *testing.T) {
	t.Parallel()
	opts := receiptOptions(t)
	receipt := receiptOf(t, opts)
	want := readOnlyBind("/certs-dir/"+localtestv2.CertificateFileName, "/accesstoken/accesstokencredentials.pfx")
	if !slices.Equal(receipt.volumes, []types.VolumeMount{want}) {
		t.Errorf("receipt volumes = %+v, want only %+v", receipt.volumes, want)
	}
	platform := opts.Topology.LocaltestBaseURL() + "/"
	if receipt.env["GeneralSettings__Hostname"] != opts.Topology.AppHostName() ||
		receipt.env["Localtest__Receipt__PlatformEndpoint"] != platform ||
		receipt.env["PlatformSettings__ApiAuthenticationEndpoint"] != platform+"authentication/api/v1/" {
		t.Errorf("receipt env = %v", receipt.env)
	}
}

func TestTheReceiptRouteIsWrittenWithItsGroup(t *testing.T) {
	t.Parallel()
	off := defaultOptions(config.DefaultImages(), t.TempDir())
	on := receiptOptions(t)
	for _, opts := range []*Options{off, on} {
		var found []envtopology.BoundTopologyRoute
		for _, route := range boundTopologyConfig(opts).Routes {
			if route.Match.Host == opts.Topology.AppHostName() && route.Match.PathPrefix == "/receipt" {
				found = append(found, route)
			}
		}
		if len(found) != 1 || found[0].Destination.URL != "http://127.0.0.1:"+hostPortReceipt ||
			metadataValue(found[0], groupMetadataKey) != groupReceipt || found[0].Enabled != (opts == on) {
			t.Errorf("receipt routes = %+v (group on: %v)", found, opts == on)
		}
	}
	for _, target := range healthTargets(on) {
		if target.name == componentReceipt && target.url != "http://127.0.0.1:"+hostPortReceipt+healthPath {
			t.Errorf("receipt health = %q", target.url)
		}
	}
}

func TestTheCorrespondenceGroupIsOffByDefault(t *testing.T) {
	t.Parallel()
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	if slices.Contains(containerNamesOf(opts), componentCorrespondence) {
		t.Error("correspondence is part of the default environment")
	}
	found := false
	for _, route := range boundTopologyConfig(opts).Routes {
		if route.Match.PathPrefix == "/correspondence" {
			found = true
			if route.Enabled || metadataValue(route, groupMetadataKey) != groupCorrespondence {
				t.Errorf("correspondence route = %+v, want it disabled and naming its group", route)
			}
		}
	}
	if !found {
		t.Error("no /correspondence route written for the proxy's 503")
	}
}

func TestTheCorrespondenceGroupStartsDialogportenAndRoutesToItsHost(t *testing.T) {
	t.Parallel()
	groups, err := ResolveGroups([]string{groupCorrespondence}, nil)
	if err != nil {
		t.Fatal(err)
	}
	opts := defaultOptions(config.DefaultImages(), t.TempDir())
	opts.Groups = groupSet(groups)
	names := containerNamesOf(opts)
	for _, want := range []string{componentCorrespondence, componentRedis, componentDialogportenWebApi} {
		if !slices.Contains(names, want) {
			t.Errorf("components = %v, want %s", names, want)
		}
	}
	if slices.Index(names, componentCorrespondence) < slices.Index(names, componentDialogportenWebApi) {
		t.Errorf("correspondence starts before the Dialogporten WebApi: %v", names)
	}

	var route envtopology.BoundTopologyRoute
	for _, r := range boundTopologyConfig(opts).Routes {
		if r.Match.Host == "local.altinn.cloud" && r.Match.PathPrefix == "/correspondence" {
			route = r
		}
	}
	if route.Destination.URL != "http://127.0.0.1:"+hostPortCorrespondence || !route.Enabled ||
		metadataValue(route, rewriteMetadataKey) != "" || metadataValue(route, groupMetadataKey) != groupCorrespondence {
		t.Errorf("correspondence route = %+v, want port %s, enabled, its group and an unchanged path", route, hostPortCorrespondence)
	}

	health := map[string]string{}
	for _, target := range healthTargets(opts) {
		health[target.name] = target.url
	}
	if want := "http://127.0.0.1:" + hostPortCorrespondence + "/health"; health[componentCorrespondence] != want {
		t.Errorf("health = %q, want %q", health[componentCorrespondence], want)
	}

	for _, c := range components(opts) {
		if c.name != componentCorrespondence {
			continue
		}
		for _, dep := range []string{
			componentPostgres, componentRegister, componentAuthorization, componentResourceRegistry,
			componentNotifications, componentEvents, componentRedis, componentDialogportenWebApi,
		} {
			if !slices.Contains(c.deps, containerOf(dep)) {
				t.Errorf("correspondence deps %v lack %s", c.deps, dep)
			}
		}
		if got := c.env["GeneralSettings__RedisConnectionString"]; got != "redis:6379,defaultDatabase=1" {
			t.Errorf("Redis = %q, want the group's Redis, database 1", got)
		}
		if len(c.volumes) != 1 || c.volumes[0].HostPath != volumeCorrespondenceBlobs || c.volumes[0].ContainerPath != "/mnt/storage" {
			t.Errorf("volumes = %+v, want the blob volume at /mnt/storage", c.volumes)
		}
	}
}
