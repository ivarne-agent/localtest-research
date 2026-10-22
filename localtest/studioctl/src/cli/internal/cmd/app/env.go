package app

import (
	"fmt"
	"maps"
	"sort"
	"strings"

	"altinn.studio/studioctl/internal/envtopology"
	statev2 "altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

// EnvKeysDirectory is the environment variable the app libraries read their data-protection keys
// directory from.
const EnvKeysDirectory = "ALTINN_KEYS_DIRECTORY"

type appEnv struct {
	values map[string]string
}

// The certificate an app signs its platform access tokens with is a *manifest.AppCertificate. The localtest
// runtime never checked those tokens, so a run against it gets nil and no signing variables; the localtest-v2
// components validate them, so a run against that runtime gets the shared test certificate.
//
// Two generations of Altinn.Common.AccessTokenClient read the settings differently: 3.x (app-lib v8) opens
// the full path, and 1.x (the v9 backend) concatenates the parent of the working directory, the keys folder
// and the file name without separators, so the keys folder is a path that resolves from there.

// newAppRunEnv builds the environment an app is started with. secretsDir is required - every run is given a
// secrets directory, and the caller has created it by now; keysDir is set only where studioctl decides where
// the data-protection keys go.
func newAppRunEnv(
	current []string,
	kestrelURL string,
	topology envtopology.Local,
	appFrontendAssetBaseUrl string,
	secretsDir string,
	keysDir string,
	signing *manifest.AppCertificate,
) []string {
	env := newAppEnv(current)
	env.addRunDefaults(kestrelURL, topology, appFrontendAssetBaseUrl, secretsDir, keysDir, signing)
	return env.entries()
}

func newAppEnv(current []string) appEnv {
	values := make(map[string]string, len(current)+16)
	for _, entry := range current {
		key, value, ok := strings.Cut(entry, "=")
		if !ok || key == "" {
			continue
		}
		if _, exists := values[key]; !exists {
			values[key] = value
		}
	}
	return appEnv{values: values}
}

// addRunDefaults sets the manifest's app section (internal/localtestv2/manifest, "app"), then what only this
// runner knows: the keys directory, the Kestrel URL, the frontend asset base URL and the OpenTelemetry endpoint.
func (e appEnv) addRunDefaults(
	kestrelURL string,
	topology envtopology.Local,
	appFrontendAssetBaseUrl string,
	secretsDir string,
	keysDir string,
	signing *manifest.AppCertificate,
) {
	// The secrets variables and STUDIOCTL_APP_RUN are fixed: where the app's secrets are and what every file
	// in that directory is called, named for the app exactly as the platform names them for a deployed app -
	// the same variables the operator's configuration map sets in every environment
	// (infra/runtime/apps-config/base/apps-runtime-common-env.yaml), because studioctl is the platform for a
	// local run. They are set on every run: the app libraries require them and fall back to nothing, and the
	// directory is there by the time a spec is built, holding the callback verification codes studioctl
	// provisions for every local run. The Maskinporten client stored with `studioctl app maskinporten set`
	// lands under the name advertised here, and a client stored while the app runs is picked up without a
	// restart. None of them is taken from the inherited environment: the contract is studioctl's to state,
	// not the shell's. The rest of the section is a default the inherited environment can override.
	app, err := manifest.MustDefault().AppEnvironment(statev2.ManifestTopology{Local: topology}, secretsDir, signing)
	if err != nil {
		// Only a change to the embedded manifest can cause this, and the golden test catches it.
		panic(fmt.Sprintf("localtest-v2 component manifest: app section: %v", err))
	}
	maps.Copy(e.values, app.Fixed)
	// Where the app persists its data-protection keys, which is the one part a run can leave unsaid: a
	// native run keeps the app libraries' default, the developer's own home directory, and only a container
	// run is told a directory, the one studioctl mounts for it, as the platform tells a deployed app.
	if keysDir != "" {
		e.values[EnvKeysDirectory] = keysDir
	}
	for key, value := range app.Defaults {
		e.setDefault(key, value)
	}
	e.setDefault("Kestrel__EndPoints__Http__Url", kestrelURL)
	if appFrontendAssetBaseUrl != "" {
		e.values["AppSettings__AppFrontendAssetBaseUrl"] = appFrontendAssetBaseUrl
	}
	e.setDefault("OTEL_EXPORTER_OTLP_ENDPOINT", topology.OTelURL())
}

func (e appEnv) setDefault(key, value string) {
	if _, exists := e.values[key]; exists {
		return
	}
	e.values[key] = value
}

func (e appEnv) entries() []string {
	keys := make([]string, 0, len(e.values))
	for key := range e.values {
		keys = append(keys, key)
	}
	sort.Strings(keys)

	entries := make([]string, 0, len(keys))
	for _, key := range keys {
		entries = append(entries, key+"="+e.values[key])
	}
	return entries
}
