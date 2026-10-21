package manifest

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"regexp"
	"slices"
	"strings"
	"testing"
)

func TestTheEmbeddedManifestParses(t *testing.T) {
	t.Parallel()
	m, err := Default()
	if err != nil {
		t.Fatal(err)
	}
	if m.Version != Version || len(m.Components) == 0 {
		t.Fatalf("version %d, %d components", m.Version, len(m.Components))
	}
}

// mutated parses the embedded manifest after mutate has changed its JSON form.
func mutated(t *testing.T, mutate func(m map[string]any)) error {
	t.Helper()
	var m map[string]any
	if err := json.Unmarshal(manifestJSON, &m); err != nil {
		t.Fatal(err)
	}
	mutate(m)
	data, err := json.Marshal(m)
	if err != nil {
		t.Fatal(err)
	}
	_, err = Parse(data)
	return err
}

func componentNamed(m map[string]any, name string) map[string]any {
	for _, c := range asList(m["components"]) {
		if obj := asObject(c); obj["name"] == name {
			return obj
		}
	}
	panic("no component " + name)
}

func TestParseRefusesWhatTheSchemaCannotCheck(t *testing.T) {
	t.Parallel()
	cases := map[string]struct {
		mutate func(m map[string]any)
		want   error
	}{
		"other version": {func(m map[string]any) { m["version"] = 2 }, errVersion},
		"duplicate component": {func(m map[string]any) {
			components := asList(m["components"])
			m["components"] = append(components, components[1])
		}, errDuplicate},
		"unknown dependency": {func(m map[string]any) {
			componentNamed(m, "storage")["dependsOn"] = []any{"blob-store"}
		}, errReference},
		"dependency cycle": {func(m map[string]any) {
			componentNamed(m, "postgres")["dependsOn"] = []any{"storage"}
		}, errCycle},
		"always-on component depending on a group": {func(m map[string]any) {
			componentNamed(m, "storage")["dependsOn"] = []any{"redis"}
		}, errGroup},
		"group depending on a group it does not imply": {func(m map[string]any) {
			componentNamed(m, "receipt")["dependsOn"] = []any{"dialogporten-webapi"}
		}, errGroup},
		"unknown group": {
			func(m map[string]any) { componentNamed(m, "receipt")["group"] = "kvittering" },
			errReference,
		},
		"group implying an unknown group": {func(m map[string]any) {
			groups := asObject(m["groups"])
			receipt := asObject(groups["receipt"])
			receipt["implies"] = []any{"storage"}
		}, errGroup},
		"unknown placeholder": {func(m map[string]any) {
			env := asObject(componentNamed(m, "storage")["environment"])
			env["X"] = "{{blobs}}"
		}, errUnknownPlaceholder},
		"placeholder without its argument": {func(m map[string]any) {
			env := asObject(componentNamed(m, "storage")["environment"])
			env["X"] = "{{alias}}"
		}, errPlaceholderArg},
		"alias of a component without one": {func(m map[string]any) {
			env := asObject(componentNamed(m, "storage")["environment"])
			env["X"] = "{{alias:register}}"
		}, errPlaceholderArg},
		"unterminated placeholder": {func(m map[string]any) {
			env := asObject(componentNamed(m, "storage")["environment"])
			env["X"] = "{{platform"
		}, errPlaceholderSyntax},
		"unlisted volume": {func(m map[string]any) {
			componentNamed(m, "storage")["mounts"] = []any{
				map[string]any{"kind": "volume", "volume": "elsewhere", "target": "/data"},
			}
		}, errMount},
		"two images": {func(m map[string]any) {
			componentNamed(m, "storage")["image"] = map[string]any{"research": "storage", "core": "pdf3"}
		}, errImage},
		"unknown third-party image": {func(m map[string]any) {
			componentNamed(m, "redis")["image"] = map[string]any{"thirdParty": "valkey"}
		}, errReference},
		"duplicate route": {func(m map[string]any) {
			componentNamed(m, "receipt")["routes"] = []any{map[string]any{"host": "{{host:app}}", "prefix": "/storage"}}
		}, errDuplicate},
		"bad health check duration": {func(m map[string]any) {
			check := asObject(componentNamed(m, "redis")["containerHealthCheck"])
			check["interval"] = "often"
		}, nil},
		"unknown field": {func(m map[string]any) { componentNamed(m, "storage")["replicas"] = 2 }, nil},
	}
	for name, tc := range cases {
		err := mutated(t, tc.mutate)
		if err == nil || !errors.Is(err, errInvalid) || (tc.want != nil && !errors.Is(err, tc.want)) {
			t.Errorf("%s: err = %v, want %v", name, err, tc.want)
		}
	}
}

type fakeTopology map[string]string

func (f fakeTopology) PublicURL(id string) (string, bool) {
	host, ok := f[id]
	return "http://" + host + ":8000", ok
}

func (f fakeTopology) PublicHost(id string) (string, bool) {
	host, ok := f[id]
	return host, ok
}

func TestThePlaceholders(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	topology := fakeTopology{"app": "local.altinn.cloud", "arbeidsflate": "af.local.altinn.cloud"}
	cases := map[string]string{
		"{{platform}}storage/api/v1/":                      "http://local.altinn.cloud:8000/storage/api/v1/",
		"{{url:app}}":                                      "http://local.altinn.cloud:8000",
		"{{url:arbeidsflate}}/api/cb":                      "http://af.local.altinn.cloud:8000/api/cb",
		"{{host:app}}":                                     "local.altinn.cloud",
		"Host={{alias:postgres}};Port={{port:postgres}}":   "Host=postgres;Port=5432",
		"{{alias:redis}}:{{port:redis}},defaultDatabase=1": "redis:6379,defaultDatabase=1",
		"{{certPath}}":                                     "/certs/localtest-accesstoken.pfx",
		"{{postgresUser}}:{{postgresPassword}}":            "postgres:postgres",
		"Password={0};{Org}/{App}":                         "Password={0};{Org}/{App}",
		"no placeholders":                                  "no placeholders",
	}
	for in, want := range cases {
		got, err := m.Expand(in, topology)
		if err != nil || got != want {
			t.Errorf("Expand(%q) = %q, %v, want %q", in, got, err, want)
		}
	}
	if _, err := m.Expand("{{url:grafana}}", topology); !errors.Is(err, errPlaceholderArg) {
		t.Errorf("a topology component the topology does not know: %v", err)
	}
}

func TestTheOverrideVariablesFollowTheRule(t *testing.T) {
	t.Parallel()
	images := MustDefault().Images
	for name, want := range map[string]string{
		"storage":          "STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE",
		"arbeidsflate-bff": "STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE_BFF",
		"redis":            "STUDIOCTL_IMAGE_LOCALTEST_V2_REDIS",
	} {
		if got := images.OverrideEnv(name); got != want {
			t.Errorf("OverrideEnv(%s) = %s, want %s", name, got, want)
		}
	}
}

func TestEveryGroupOwnsAComponentOrImpliesOthers(t *testing.T) {
	t.Parallel()
	m := MustDefault()
	for _, name := range m.GroupNames() {
		owns := slices.ContainsFunc(m.Components, func(c Component) bool { return c.Group == name })
		if !owns && len(m.Implies(name)) == 0 {
			t.Errorf("group %s has no component and implies nothing", name)
		}
	}
	if got := m.Implies(
		"everything",
	); !slices.Equal(
		got,
		[]string{"arbeidsflate", "correspondence", "dialogporten", "receipt"},
	) {
		t.Errorf("everything implies %v", got)
	}
	closure := m.Closure("correspondence")
	if len(closure) != 2 || !closure["correspondence"] || !closure["dialogporten"] {
		t.Errorf("closure of correspondence = %v", closure)
	}
}

// TestThePostgresInitScriptsAreThoseThePostgresImageCopies reads localtest/postgres/Dockerfile when the test runs in the
// localtest-research repository; upstream, where that file does not exist, it is skipped.
func TestThePostgresInitScriptsAreThoseThePostgresImageCopies(t *testing.T) {
	t.Parallel()
	dockerfile := filepath.Join("..", "..", "..", "..", "..", "..", "postgres", "Dockerfile")
	data, err := os.ReadFile(dockerfile)
	if errors.Is(err, os.ErrNotExist) {
		t.Skip("no localtest/postgres/Dockerfile beside this tree")
	}
	if err != nil {
		t.Fatal(err)
	}
	matches := regexp.MustCompile(`(?m)^COPY (\S+) (\S+)$`).FindAllStringSubmatch(string(data), -1)
	copied := make([]InitScript, 0, len(matches))
	for _, match := range matches {
		copied = append(copied, InitScript{Source: match[1], Target: match[2]})
	}
	if got := MustDefault().Postgres.InitScripts; !slices.Equal(got, copied) {
		t.Errorf("postgres.initScripts = %v, want what %s copies: %v", got, dockerfile, copied)
	}
	for _, script := range copied {
		path := filepath.Join(filepath.Dir(filepath.Dir(dockerfile)), filepath.FromSlash(script.Source))
		if _, err := os.Stat(path); err != nil {
			t.Errorf("init script %s: %v", script.Source, err)
		}
	}
}

// TestEveryDevBuildNamesADockerfileThatExists checks the dev builds against the localtest-research checkout this tree
// sits in; upstream it is skipped.
func TestEveryDevBuildNamesADockerfileThatExists(t *testing.T) {
	t.Parallel()
	root := filepath.Join("..", "..", "..", "..", "..", "..")
	if _, err := os.Stat(filepath.Join(root, "postgres", "Dockerfile")); errors.Is(err, os.ErrNotExist) {
		t.Skip("not in the localtest-research checkout")
	}
	for _, c := range MustDefault().Components {
		if c.Dev == nil {
			continue
		}
		dockerfile := c.Dev.Dockerfile
		if dockerfile == "" {
			dockerfile = DefaultDockerfile
		}
		path := filepath.Join(root, filepath.FromSlash(c.Dev.Context), filepath.FromSlash(dockerfile))
		if _, err := os.Stat(path); err != nil {
			t.Errorf("%s: %v", c.Name, err)
		}
		if strings.HasPrefix(c.Dev.Context, "/") || strings.Contains(c.Dev.Context, "..") {
			t.Errorf("%s: dev context %q must stay inside the localtest folder", c.Name, c.Dev.Context)
		}
	}
}
