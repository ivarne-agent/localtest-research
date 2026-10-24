package localtestv2

const (
	// AppsNetwork is the network container-mode apps join (studioctl run --mode container): env up creates it beside
	// the stack's network and attaches the front door to both, so the front door reaches an app by its alias while the
	// app sees none of the components; the app calls the platform through the public origin, as a process app does.
	// env down removes it, with any app container still on it.
	AppsNetwork = "localtest-v2_apps"

	// Label marks what belongs to the runtime beside its resource graph: the apps network (LabelAppsNetwork) and the
	// app containers on it (LabelAppContainer), which env down removes before the network.
	Label = "altinn.studio/localtest-v2"
	// LabelAppsNetwork is Label's value on the apps network.
	LabelAppsNetwork = "apps-network"
	// LabelAppContainer is Label's value on an app container on the apps network.
	LabelAppContainer = "app"
)
