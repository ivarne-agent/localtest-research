package localtestv2

import (
	"errors"
	"fmt"
	"slices"
	"strings"
)

// groupDialogporten is the optional component group of Dialogporten: Redis and the WebApi, GraphQL and Service hosts.
// Apps never call Dialogporten, so the default stack leaves it out and saves about a gigabyte of memory.
const groupDialogporten = "dialogporten"

// dialogportenOnByDefault is the one place the group's default lives: false starts it only with `--with dialogporten`;
// true starts it unless `--without dialogporten` says otherwise.
const dialogportenOnByDefault = false

var errUnknownGroup = errors.New("unknown component group")

// groupDefaults is every optional group with whether `env up` starts it when no flag names it.
func groupDefaults() map[string]bool {
	return map[string]bool{groupDialogporten: dialogportenOnByDefault}
}

// GroupNames lists the optional component groups, for `--with` and `--without`.
func GroupNames() []string {
	names := make([]string, 0, len(groupDefaults()))
	for name := range groupDefaults() {
		names = append(names, name)
	}
	slices.Sort(names)
	return names
}

// ResolveGroups turns the defaults and the `--with` and `--without` names into the enabled groups, sorted.
func ResolveGroups(with, without []string) ([]string, error) {
	enabled := groupDefaults()
	for _, change := range []struct {
		names []string
		on    bool
	}{{with, true}, {without, false}} {
		for _, name := range change.names {
			if _, known := enabled[name]; !known {
				return nil, fmt.Errorf("%w: %s (one of %s)", errUnknownGroup, name, strings.Join(GroupNames(), ", "))
			}
			enabled[name] = change.on
		}
	}
	var out []string
	for name, on := range enabled {
		if on {
			out = append(out, name)
		}
	}
	slices.Sort(out)
	return out, nil
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
func allGroups() map[string]bool {
	return groupSet(GroupNames())
}
