package localtestv2

import (
	"errors"
	"fmt"
	"maps"
	"slices"
	"strings"
)

var (
	errUnknownGroup        = errors.New("unknown component group")
	errImpliedGroupRemoved = errors.New("component group left out although an enabled group needs it")
)

// group is one optional component group: whether `env up` starts it when no flag names it, and the groups it needs.
type group struct {
	// implies are the groups that start with this one, because its components cannot run without them.
	implies     []string
	onByDefault bool
}

// groupTable is every optional group, its default and the groups it implies directly, from the component manifest.
func groupTable() map[string]group {
	m := stack()
	table := make(map[string]group, len(m.Groups))
	for name, g := range m.Groups {
		table[name] = group{implies: m.Implies(name), onByDefault: g.OnByDefault}
	}
	return table
}

// GroupNames lists the optional component groups, for `--with` and `--without`.
func GroupNames() []string {
	return slices.Sorted(maps.Keys(groupTable()))
}

// ResolveGroups turns the defaults and the `--with` and `--without` names into the enabled groups, sorted.
//
// The groups that are on by default and those named by `--with` are enabled, then those named by `--without` are
// removed, then every enabled group brings in the groups it implies, transitively. `--without` removes only the named
// group, never what it implies. A group left out with `--without` that an enabled group implies is an error: the
// enabled group cannot run without it, so `--with correspondence --without dialogporten` fails.
func ResolveGroups(with, without []string) ([]string, error) {
	table := groupTable()
	for _, name := range slices.Concat(with, without) {
		if _, known := table[name]; !known {
			return nil, fmt.Errorf("%w: %s (one of %s)", errUnknownGroup, name, strings.Join(GroupNames(), ", "))
		}
	}

	requested := map[string]bool{}
	for name, g := range table {
		requested[name] = g.onByDefault
	}
	for _, name := range with {
		requested[name] = true
	}
	for _, name := range without {
		requested[name] = false
	}

	enabled := map[string]bool{}
	for _, name := range slices.Sorted(maps.Keys(requested)) {
		if !requested[name] {
			continue
		}
		for _, implied := range slices.Sorted(maps.Keys(stack().Closure(name))) {
			enabled[implied] = true
			if slices.Contains(without, implied) {
				return nil, fmt.Errorf(
					"%w: %s implies %s and cannot run without it; drop --without %s or leave out %s",
					errImpliedGroupRemoved, name, implied, implied, name,
				)
			}
		}
	}
	return slices.Sorted(maps.Keys(enabled)), nil
}

// groupSet is the Options form of a list of enabled groups.
func groupSet(names []string) map[string]bool {
	set := make(map[string]bool, len(names))
	for _, name := range names {
		set[name] = true
	}
	return set
}

// allGroups enables every optional group, which is what teardown uses to find the containers of whichever were started.
// A group without components, such as everything, adds nothing.
func allGroups() map[string]bool {
	return groupSet(GroupNames())
}
