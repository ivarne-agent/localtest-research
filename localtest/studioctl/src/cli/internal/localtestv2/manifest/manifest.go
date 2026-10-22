// Package manifest holds the localtest-v2 component manifest: the one description of the stack's containers, groups,
// image pin, Postgres init scripts and front-door routes, which studioctl builds its runtime from and other runtimes can
// read as JSON. The manifest and its JSON Schema are embedded; see README.md beside them.
package manifest

import (
	"bytes"
	_ "embed"
	"encoding/json"
	"errors"
	"fmt"
	"maps"
	"slices"
	"strings"
	"sync"
	"time"
)

// Version is the manifest format version this package reads.
const Version = 1

// DefaultDockerfile is a dev build's Dockerfile when the manifest names none.
const DefaultDockerfile = "Dockerfile"

// The embedded files. The schema is embedded for the tests and for readers that want it from the binary.
var (
	//go:embed manifest.json
	manifestJSON []byte

	//go:embed manifest.schema.json
	schemaJSON []byte
)

var errInvalid = errors.New("invalid localtest-v2 manifest")

// JSON returns the embedded manifest as written.
func JSON() []byte {
	return bytes.Clone(manifestJSON)
}

// SchemaJSON returns the embedded JSON Schema of the manifest.
func SchemaJSON() []byte {
	return bytes.Clone(schemaJSON)
}

// Manifest is the parsed manifest. Field documentation is in manifest.schema.json.
type Manifest struct {
	Schema          string           `json:"$schema"`
	Groups          map[string]Group `json:"groups"`
	Images          Images           `json:"images"`
	ContainerPrefix string           `json:"containerPrefix"`
	Network         string           `json:"network"`
	Certificate     Certificate      `json:"certificate"`
	FrontDoor       FrontDoor        `json:"frontDoor"`
	Postgres        Postgres         `json:"postgres"`
	Volumes         []Volume         `json:"volumes"`
	Components      []Component      `json:"components"`
	App             App              `json:"app"`
	Version         int              `json:"version"`
}

// loadDefault parses the embedded manifest on first use; the runtime asks for it many times per command.
//
//nolint:gochecknoglobals // A parse-once cache of embedded, immutable data.
var loadDefault = sync.OnceValues(func() (*Manifest, error) {
	return Parse(manifestJSON)
})

// Default returns the embedded manifest, parsed and checked once.
func Default() (*Manifest, error) {
	return loadDefault()
}

// MustDefault returns the embedded manifest and panics when it is invalid, which only a change to the embedded file can
// cause and the package's tests catch.
func MustDefault() *Manifest {
	m, err := Default()
	if err != nil {
		panic(err)
	}
	return m
}

// Certificate is the shared test certificate.
type Certificate struct {
	FileName     string `json:"fileName"`
	ContainerDir string `json:"containerDir"`
}

// ContainerPath is the certificate's path in a container that mounts its directory.
func (c Certificate) ContainerPath() string {
	return c.ContainerDir + "/" + c.FileName
}

// Images says where the images come from.
type Images struct {
	ThirdParty        map[string]ExternalImage `json:"thirdParty"`
	Core              map[string]ExternalImage `json:"core"`
	RepositoryPrefix  string                   `json:"repositoryPrefix"`
	Tag               string                   `json:"tag"`
	TagEnv            string                   `json:"tagEnv"`
	OverrideEnvPrefix string                   `json:"overrideEnvPrefix"`
}

// OverrideEnv is the per-image override variable of a research or third-party image: the prefix plus the name
// upper-cased with - turned into _.
func (i Images) OverrideEnv(name string) string {
	return i.OverrideEnvPrefix + strings.ToUpper(strings.ReplaceAll(name, "-", "_"))
}

// ResearchNames lists the research images the components use, in component order.
func (m *Manifest) ResearchNames() []string {
	var names []string
	for _, c := range m.Components {
		if c.Image.Research != "" && !slices.Contains(names, c.Image.Research) {
			names = append(names, c.Image.Research)
		}
	}
	return names
}

// ExternalImage is an image that is not one of the research builds.
type ExternalImage struct {
	Description string `json:"description"`
	Repository  string `json:"repository"`
	Tag         string `json:"tag"`
	OverrideEnv string `json:"overrideEnv"`
	Floating    bool   `json:"floating"`
}

// Volume is a named volume.
type Volume struct {
	Name        string `json:"name"`
	Description string `json:"description"`
}

// VolumeNames lists the named volumes.
func (m *Manifest) VolumeNames() []string {
	names := make([]string, 0, len(m.Volumes))
	for _, v := range m.Volumes {
		names = append(names, v.Name)
	}
	return names
}

// Postgres is the shared Postgres server.
type Postgres struct {
	Component   string       `json:"component"`
	User        string       `json:"user"`
	Password    string       `json:"password"`
	InitScripts []InitScript `json:"initScripts"`
}

// InitScript is one file baked into the Postgres image.
type InitScript struct {
	Source string `json:"source"`
	Target string `json:"target"`
}

// FrontDoor is what the front door needs beyond the components' routes.
type FrontDoor struct {
	HealthPath string      `json:"healthPath"`
	HostRoutes []HostRoute `json:"hostRoutes"`
}

// HostRoute is a route to a loopback port on the host.
type HostRoute struct {
	Name        string `json:"name"`
	Description string `json:"description"`
	Host        string `json:"host"`
	Prefix      string `json:"prefix"`
	HostPort    int    `json:"hostPort"`
}

// Group is an optional component group.
type Group struct {
	Description string   `json:"description"`
	Implies     []string `json:"implies"`
	OnByDefault bool     `json:"onByDefault"`
	ImpliesAll  bool     `json:"impliesAll"`
}

// Component is one container.
type Component struct {
	Environment           map[string]string `json:"environment"`
	ContainerHealthCheck  *HealthCheck      `json:"containerHealthCheck"`
	Dev                   *DevBuild         `json:"dev"`
	IngressHosts          *bool             `json:"ingressHosts"`
	Image                 ImageSource       `json:"image"`
	Name                  string            `json:"name"`
	Description           string            `json:"description"`
	Group                 string            `json:"group"`
	HealthPath            string            `json:"healthPath"`
	Notes                 []string          `json:"notes"`
	Command               []string          `json:"command"`
	Mounts                []Mount           `json:"mounts"`
	DependsOn             []string          `json:"dependsOn"`
	NetworkAliases        []string          `json:"networkAliases"`
	ExtraHostGatewayNames []string          `json:"extraHostGatewayNames"`
	Routes                []Route           `json:"routes"`
	ContainerPort         int               `json:"containerPort"`
	HostPort              int               `json:"hostPort"`
	HostUser              bool              `json:"hostUser"`
}

// MapsIngressHosts reports whether the component's container resolves the public host names to the host gateway.
func (c Component) MapsIngressHosts() bool {
	return c.IngressHosts == nil || *c.IngressHosts
}

// DevSelector is the --dev name of the component's dev build, or empty when it has none.
func (c Component) DevSelector() string {
	if c.Dev == nil {
		return ""
	}
	if c.Dev.Selector != "" {
		return c.Dev.Selector
	}
	return c.Name
}

// ImageSource names the component's image: exactly one field is set.
type ImageSource struct {
	Research   string `json:"research"`
	ThirdParty string `json:"thirdParty"`
	Core       string `json:"core"`
}

// DevBuild is how `--dev` builds a component.
type DevBuild struct {
	Selector   string `json:"selector"`
	Context    string `json:"context"`
	Dockerfile string `json:"dockerfile"`
}

// HealthCheck is a container health check. Durations are Go durations.
type HealthCheck struct {
	Interval    string   `json:"interval"`
	Timeout     string   `json:"timeout"`
	StartPeriod string   `json:"startPeriod"`
	Test        []string `json:"test"`
	Retries     int      `json:"retries"`
}

// Durations parses the check's durations; a missing start period is zero.
func (h HealthCheck) Durations() (interval, timeout, startPeriod time.Duration, err error) {
	if interval, err = time.ParseDuration(h.Interval); err != nil {
		return 0, 0, 0, fmt.Errorf("interval: %w", err)
	}
	if timeout, err = time.ParseDuration(h.Timeout); err != nil {
		return 0, 0, 0, fmt.Errorf("timeout: %w", err)
	}
	if h.StartPeriod != "" {
		if startPeriod, err = time.ParseDuration(h.StartPeriod); err != nil {
			return 0, 0, 0, fmt.Errorf("startPeriod: %w", err)
		}
	}
	return interval, timeout, startPeriod, nil
}

// Mount kinds.
const (
	MountCertificateDir  = "certificateDir"
	MountCertificateFile = "certificateFile"
	MountVolume          = "volume"
)

// Mount is one mount; Kind says which fields apply.
type Mount struct {
	Kind   string `json:"kind"`
	Volume string `json:"volume"`
	Target string `json:"target"`
}

// Route is a public host-and-prefix route.
type Route struct {
	Host    string `json:"host"`
	Prefix  string `json:"prefix"`
	Rewrite string `json:"rewrite"`
}

// Component returns the named component.
func (m *Manifest) Component(name string) (Component, bool) {
	for _, c := range m.Components {
		if c.Name == name {
			return c, true
		}
	}
	var zero Component
	return zero, false
}

// GroupNames lists the groups, sorted.
func (m *Manifest) GroupNames() []string {
	return slices.Sorted(maps.Keys(m.Groups))
}

// Parse reads a manifest and checks what the schema cannot: references between its parts, start order and
// placeholders. Unknown fields are refused.
func Parse(data []byte) (*Manifest, error) {
	var m Manifest
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&m); err != nil {
		return nil, fmt.Errorf("%w: %w", errInvalid, err)
	}
	if err := m.check(); err != nil {
		return nil, fmt.Errorf("%w: %w", errInvalid, err)
	}
	return &m, nil
}
