package manifest

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

// Placeholders: a placeholder is {{name}} or {{name:argument}} in an environment value, a command argument, a container
// health check argument or a route host. Single braces are not placeholders ({0} and {Org} belong to the components).
const (
	// PlaceholderPlatform is the public origin with a trailing slash (http://local.altinn.cloud:8000/), the value of
	// every ...PlatformEndpoint setting: {{url:app}}/.
	PlaceholderPlatform = "platform"
	// PlaceholderURL is the public origin of a topology component without a trailing slash: {{url:app}} is
	// http://local.altinn.cloud:8000, {{url:arbeidsflate}} http://af.local.altinn.cloud:8000.
	PlaceholderURL = "url"
	// PlaceholderHost is the public host name of a topology component: {{host:app}} is local.altinn.cloud.
	PlaceholderHost = "host"
	// PlaceholderAlias is the first network alias of a component: {{alias:postgres}} is postgres.
	PlaceholderAlias = "alias"
	// PlaceholderPort is the container port of a component: {{port:postgres}} is 5432.
	PlaceholderPort = "port"
	// PlaceholderCertPath is the shared certificate's path in a container that mounts its directory.
	PlaceholderCertPath = "certPath"
	// PlaceholderPostgresUser is postgres.user.
	PlaceholderPostgresUser = "postgresUser"
	// PlaceholderPostgresPassword is postgres.password.
	PlaceholderPostgresPassword = "postgresPassword"
)

// TopologyApp is the topology component of the public origin, which {{platform}} names.
const TopologyApp = "app"

const (
	placeholderOpen  = "{{"
	placeholderClose = "}}"
)

var (
	errPlaceholderSyntax  = errors.New("unterminated placeholder")
	errUnknownPlaceholder = errors.New("unknown placeholder")
	errPlaceholderArg     = errors.New("bad placeholder argument")
)

// Topology resolves the topology placeholders ({{url:...}}, {{host:...}}); envtopology's component IDs are the
// arguments.
type Topology interface {
	PublicURL(component string) (string, bool)
	PublicHost(component string) (string, bool)
}

// placeholder is one parsed {{name:argument}}.
type placeholder struct {
	name string
	arg  string
}

// takesArgument reports whether a placeholder name needs an argument, and whether the name is known.
func takesArgument(name string) (needsArg, known bool) {
	switch name {
	case PlaceholderURL, PlaceholderHost, PlaceholderAlias, PlaceholderPort:
		return true, true
	case PlaceholderPlatform, PlaceholderCertPath, PlaceholderPostgresUser, PlaceholderPostgresPassword:
		return false, true
	default:
		return false, false
	}
}

// expand replaces every placeholder in s with what resolve returns for it.
func expand(s string, resolve func(placeholder) (string, error)) (string, error) {
	var out strings.Builder
	rest := s
	for {
		start := strings.Index(rest, placeholderOpen)
		if start < 0 {
			out.WriteString(rest)
			return out.String(), nil
		}
		out.WriteString(rest[:start])
		rest = rest[start+len(placeholderOpen):]
		end := strings.Index(rest, placeholderClose)
		if end < 0 {
			return "", fmt.Errorf("%w in %q", errPlaceholderSyntax, s)
		}
		p, err := parsePlaceholder(rest[:end])
		if err != nil {
			return "", fmt.Errorf("%w in %q", err, s)
		}
		value, err := resolve(p)
		if err != nil {
			return "", fmt.Errorf("{{%s}} in %q: %w", rest[:end], s, err)
		}
		out.WriteString(value)
		rest = rest[end+len(placeholderClose):]
	}
}

func parsePlaceholder(text string) (placeholder, error) {
	name, arg, hasArg := strings.Cut(text, ":")
	needsArg, known := takesArgument(name)
	if !known {
		return placeholder{}, fmt.Errorf("%w {{%s}}", errUnknownPlaceholder, text)
	}
	if needsArg != hasArg || (hasArg && arg == "") {
		return placeholder{}, fmt.Errorf("%w {{%s}}", errPlaceholderArg, text)
	}
	return placeholder{name: name, arg: arg}, nil
}

// Expand resolves the placeholders in s against the manifest and the topology.
func (m *Manifest) Expand(s string, topology Topology) (string, error) {
	return expand(s, func(p placeholder) (string, error) {
		return m.resolve(p, topology)
	})
}

// ExpandAll resolves the placeholders in every string of values; nil stays nil.
func (m *Manifest) ExpandAll(values []string, topology Topology) ([]string, error) {
	if values == nil {
		return nil, nil
	}
	out := make([]string, 0, len(values))
	for _, v := range values {
		expanded, err := m.Expand(v, topology)
		if err != nil {
			return nil, err
		}
		out = append(out, expanded)
	}
	return out, nil
}

func (m *Manifest) resolve(p placeholder, topology Topology) (string, error) {
	switch p.name {
	case PlaceholderPlatform:
		return topologyValue(topology.PublicURL, TopologyApp, "/")
	case PlaceholderURL:
		return topologyValue(topology.PublicURL, p.arg, "")
	case PlaceholderHost:
		return topologyValue(topology.PublicHost, p.arg, "")
	case PlaceholderCertPath:
		return m.Certificate.ContainerPath(), nil
	case PlaceholderPostgresUser:
		return m.Postgres.User, nil
	case PlaceholderPostgresPassword:
		return m.Postgres.Password, nil
	case PlaceholderAlias, PlaceholderPort:
		return m.componentValue(p)
	}
	return "", fmt.Errorf("%w {{%s}}", errUnknownPlaceholder, p.name)
}

func topologyValue(lookup func(string) (string, bool), component, suffix string) (string, error) {
	value, ok := lookup(component)
	if !ok {
		return "", fmt.Errorf("%w: no topology component %q", errPlaceholderArg, component)
	}
	return value + suffix, nil
}

func (m *Manifest) componentValue(p placeholder) (string, error) {
	c, ok := m.Component(p.arg)
	if !ok {
		return "", fmt.Errorf("%w: no component %q", errPlaceholderArg, p.arg)
	}
	if p.name == PlaceholderPort {
		return strconv.Itoa(c.ContainerPort), nil
	}
	if len(c.NetworkAliases) == 0 {
		return "", fmt.Errorf("%w: component %q has no network alias", errPlaceholderArg, p.arg)
	}
	return c.NetworkAliases[0], nil
}

// templatedStrings lists every string of the component that may hold placeholders, for checks.
func (c Component) templatedStrings() []string {
	var out []string
	for _, key := range sortedKeys(c.Environment) {
		out = append(out, c.Environment[key])
	}
	out = append(out, c.Command...)
	if c.ContainerHealthCheck != nil {
		out = append(out, c.ContainerHealthCheck.Test...)
	}
	for _, r := range c.Routes {
		out = append(out, r.Host)
	}
	return out
}
