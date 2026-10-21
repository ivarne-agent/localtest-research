package manifest

import (
	"errors"
	"fmt"
	"maps"
	"slices"
	"strings"
)

var (
	errVersion   = errors.New("unsupported manifest version")
	errDuplicate = errors.New("duplicate")
	errReference = errors.New("unknown reference")
	errCycle     = errors.New("dependency cycle")
	errImage     = errors.New("image must name exactly one of research, thirdParty or core")
	errMount     = errors.New("bad mount")
	errGroup     = errors.New("bad group")
)

// check verifies what the JSON Schema cannot express: that names are unique and every reference resolves, that the
// dependencies have no cycle and a component only depends on components that run whenever it does, and that every
// placeholder is known and names an existing component.
func (m *Manifest) check() error {
	if m.Version != Version {
		return fmt.Errorf("%w %d (this studioctl reads %d)", errVersion, m.Version, Version)
	}
	if err := m.checkGroups(); err != nil {
		return err
	}
	volumes, err := m.volumeSet()
	if err != nil {
		return err
	}
	byName := map[string]Component{}
	for _, c := range m.Components {
		if _, dup := byName[c.Name]; dup {
			return fmt.Errorf("%w component %q", errDuplicate, c.Name)
		}
		byName[c.Name] = c
	}
	for _, c := range m.Components {
		if err := m.checkComponent(c, byName, volumes); err != nil {
			return fmt.Errorf("component %q: %w", c.Name, err)
		}
	}
	if err := checkCycles(m.Components, byName); err != nil {
		return err
	}
	if err := m.checkRoutes(); err != nil {
		return err
	}
	if pg, ok := byName[m.Postgres.Component]; !ok || pg.Group != "" {
		return fmt.Errorf("%w: postgres.component %q is not an always-on component", errReference, m.Postgres.Component)
	}
	return nil
}

func (m *Manifest) volumeSet() (map[string]bool, error) {
	volumes := map[string]bool{}
	for _, v := range m.Volumes {
		if volumes[v.Name] {
			return nil, fmt.Errorf("%w volume %q", errDuplicate, v.Name)
		}
		volumes[v.Name] = true
	}
	return volumes, nil
}

// checkRoutes refuses two routes, of components or to the host, on one host and prefix.
func (m *Manifest) checkRoutes() error {
	owners := map[string]string{}
	claim := func(host, prefix, owner string) error {
		if other, dup := owners[host+prefix]; dup {
			return fmt.Errorf("%w route %s%s (%s and %s)", errDuplicate, host, prefix, other, owner)
		}
		owners[host+prefix] = owner
		return nil
	}
	for _, c := range m.Components {
		for _, r := range c.Routes {
			if err := claim(r.Host, r.Prefix, c.Name); err != nil {
				return err
			}
		}
	}
	for _, r := range m.FrontDoor.HostRoutes {
		if err := claim(r.Host, r.Prefix, r.Name); err != nil {
			return err
		}
		if err := checkPlaceholders(m, r.Host); err != nil {
			return fmt.Errorf("host route %q: %w", r.Name, err)
		}
	}
	return nil
}

func (m *Manifest) checkGroups() error {
	for name, g := range m.Groups {
		for _, implied := range g.Implies {
			if _, known := m.Groups[implied]; !known || implied == name {
				return fmt.Errorf("%w %q implies %q", errGroup, name, implied)
			}
		}
	}
	return nil
}

func (m *Manifest) checkComponent(c Component, byName map[string]Component, volumes map[string]bool) error {
	if c.Group != "" {
		if _, known := m.Groups[c.Group]; !known {
			return fmt.Errorf("%w group %q", errReference, c.Group)
		}
	}
	if err := m.checkImage(c.Image); err != nil {
		return err
	}
	for _, dep := range c.DependsOn {
		d, ok := byName[dep]
		if !ok {
			return fmt.Errorf("%w dependency %q", errReference, dep)
		}
		if d.Group != "" && !m.Closure(c.Group)[d.Group] {
			return fmt.Errorf("%w: dependency %q is in group %q, which does not run whenever %q does",
				errGroup, dep, d.Group, c.Name)
		}
	}
	for _, mount := range c.Mounts {
		if err := checkMount(mount, volumes); err != nil {
			return err
		}
	}
	if c.ContainerHealthCheck != nil {
		if _, _, _, err := c.ContainerHealthCheck.Durations(); err != nil {
			return fmt.Errorf("containerHealthCheck: %w", err)
		}
	}
	for _, s := range c.templatedStrings() {
		if err := checkPlaceholders(m, s); err != nil {
			return err
		}
	}
	return nil
}

func (m *Manifest) checkImage(image ImageSource) error {
	set := 0
	for _, name := range []string{image.Research, image.ThirdParty, image.Core} {
		if name != "" {
			set++
		}
	}
	if set != 1 {
		return errImage
	}
	if _, ok := m.Images.ThirdParty[image.ThirdParty]; image.ThirdParty != "" && !ok {
		return fmt.Errorf("%w third-party image %q", errReference, image.ThirdParty)
	}
	if _, ok := m.Images.Core[image.Core]; image.Core != "" && !ok {
		return fmt.Errorf("%w core image %q", errReference, image.Core)
	}
	return nil
}

func checkMount(mount Mount, volumes map[string]bool) error {
	switch mount.Kind {
	case MountCertificateDir:
		if mount.Target != "" || mount.Volume != "" {
			return fmt.Errorf("%w: %s takes no target or volume", errMount, mount.Kind)
		}
	case MountCertificateFile:
		if mount.Target == "" || mount.Volume != "" {
			return fmt.Errorf("%w: %s takes a target only", errMount, mount.Kind)
		}
	case MountVolume:
		if mount.Target == "" || !volumes[mount.Volume] {
			return fmt.Errorf("%w: volume %q at %q (volumes must be listed)", errMount, mount.Volume, mount.Target)
		}
	default:
		return fmt.Errorf("%w kind %q", errMount, mount.Kind)
	}
	return nil
}

// checkCycles fails on the first dependency cycle, depth first in component order.
func checkCycles(components []Component, byName map[string]Component) error {
	const (
		visiting = 1
		done     = 2
	)
	state := map[string]int{}
	var visit func(name string, path []string) error
	visit = func(name string, path []string) error {
		switch state[name] {
		case visiting:
			return fmt.Errorf("%w: %s", errCycle, strings.Join(append(path, name), " -> "))
		case done:
			return nil
		}
		state[name] = visiting
		for _, dep := range byName[name].DependsOn {
			if err := visit(dep, append(path, name)); err != nil {
				return err
			}
		}
		state[name] = done
		return nil
	}
	for _, c := range components {
		if err := visit(c.Name, nil); err != nil {
			return err
		}
	}
	return nil
}

// checkPlaceholders parses every placeholder in s and resolves those the manifest can resolve on its own.
func checkPlaceholders(m *Manifest, s string) error {
	_, err := expand(s, func(p placeholder) (string, error) {
		switch p.name {
		case PlaceholderURL, PlaceholderHost, PlaceholderPlatform:
			return "", nil
		default:
			return m.resolve(p, nil)
		}
	})
	return err
}

// Closure is the named group and every group it implies, transitively; the empty name (an always-on component) has
// an empty closure.
func (m *Manifest) Closure(name string) map[string]bool {
	seen := map[string]bool{}
	if name == "" {
		return seen
	}
	pending := []string{name}
	for len(pending) > 0 {
		next := pending[0]
		pending = pending[1:]
		if seen[next] {
			continue
		}
		seen[next] = true
		pending = append(pending, m.Implies(next)...)
	}
	return seen
}

// Implies lists the groups a group implies directly, sorted; a group with impliesAll implies every other group.
func (m *Manifest) Implies(name string) []string {
	g := m.Groups[name]
	if !g.ImpliesAll {
		return slices.Sorted(slices.Values(g.Implies))
	}
	var others []string
	for _, other := range slices.Sorted(maps.Keys(m.Groups)) {
		if other != name {
			others = append(others, other)
		}
	}
	return others
}

func sortedKeys(values map[string]string) []string {
	return slices.Sorted(maps.Keys(values))
}
