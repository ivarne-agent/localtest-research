package localtestv2

import "altinn.studio/studioctl/internal/envtopology"

// ManifestTopology resolves the component manifest's topology placeholders ({{url:...}}, {{host:...}}) against
// envtopology: the runtime's components and an app's environment (the manifest's app section) use it alike.
type ManifestTopology struct {
	Local envtopology.Local
}

// PublicURL is the public origin of a topology component, without a trailing slash.
func (t ManifestTopology) PublicURL(id string) (string, bool) {
	if _, ok := t.Local.Component(envtopology.ComponentID(id)); !ok {
		return "", false
	}
	return t.Local.PublicBaseURL(envtopology.ComponentID(id)), true
}

// PublicHost is the public host name of a topology component.
func (t ManifestTopology) PublicHost(id string) (string, bool) {
	component, ok := t.Local.Component(envtopology.ComponentID(id))
	if !ok {
		return "", false
	}
	return component.Host(), true
}
