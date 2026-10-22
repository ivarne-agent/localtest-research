package manifest

import (
	"errors"
	"fmt"
	"maps"
	"slices"
)

// App is the environment every runner starts an app with: studioctl's `run` and `app env`, and the Aspire package
// (localtest/aspire/Altinn.App.Aspire). The runner adds what only it knows: the Kestrel URL, the frontend asset base
// URL, the OpenTelemetry endpoint, the data-protection keys directory and the value of the secrets directory.
type App struct {
	Environment        map[string]string `json:"environment"`
	FixedEnvironment   map[string]string `json:"fixedEnvironment"`
	AccessTokenSigning map[string]string `json:"accessTokenSigning"`
	Description        string            `json:"description"`
	Secrets            AppSecrets        `json:"secrets"`
}

// AppSecrets names the app's secrets directory variable and the files in it, as the platform names them for a
// deployed app.
type AppSecrets struct {
	DirectoryEnv string          `json:"directoryEnv"`
	Files        []AppSecretFile `json:"files"`
}

// AppSecretFile is one file of the secrets directory: the variable that names it and its name.
type AppSecretFile struct {
	Name              string `json:"name"`
	Description       string `json:"description"`
	Env               string `json:"env"`
	FileName          string `json:"fileName"`
	WrittenOnEveryRun bool   `json:"writtenOnEveryRun"`
}

// SecretFile returns the named secrets file.
func (s AppSecrets) SecretFile(name string) (AppSecretFile, bool) {
	for _, f := range s.Files {
		if f.Name == name {
			return f, true
		}
	}
	var zero AppSecretFile
	return zero, false
}

// AppCertificate is where an app finds the shared test certificate, which only its runner knows: Path is the
// certificate as the app opens it ({{appCertPath}}), KeysFolder the directory as the 1.x access token client resolves
// it, appended to the parent of the app's working directory, with a leading and a trailing slash
// ({{appCertKeysFolder}}).
type AppCertificate struct {
	Path       string
	KeysFolder string
}

// AppEnvironment is the app section resolved for one run. Defaults are set unless the app's inherited environment
// has the variable; Fixed is always set.
type AppEnvironment struct {
	Defaults map[string]string
	Fixed    map[string]string
}

var errAppKey = errors.New("bad app environment variable")

// AppEnvironment resolves the app section: the environment and the fixed environment against the topology, the
// secrets variables with secretsDir as the directory, and the access token signing variables when the runner has
// the certificate (certificate not nil).
func (m *Manifest) AppEnvironment(topology Topology, secretsDir string, certificate *AppCertificate) (
	AppEnvironment, error,
) {
	env := AppEnvironment{Defaults: map[string]string{}, Fixed: map[string]string{}}
	if err := m.expandInto(env.Defaults, m.App.Environment, topology, nil); err != nil {
		return AppEnvironment{}, fmt.Errorf("app.environment: %w", err)
	}
	if certificate != nil {
		if err := m.expandInto(env.Defaults, m.App.AccessTokenSigning, topology, certificate); err != nil {
			return AppEnvironment{}, fmt.Errorf("app.accessTokenSigning: %w", err)
		}
	}
	if err := m.expandInto(env.Fixed, m.App.FixedEnvironment, topology, nil); err != nil {
		return AppEnvironment{}, fmt.Errorf("app.fixedEnvironment: %w", err)
	}
	env.Fixed[m.App.Secrets.DirectoryEnv] = secretsDir
	for _, f := range m.App.Secrets.Files {
		env.Fixed[f.Env] = f.FileName
	}
	return env, nil
}

func (m *Manifest) expandInto(
	out, values map[string]string, topology Topology, certificate *AppCertificate,
) error {
	for _, key := range sortedKeys(values) {
		value, err := expand(values[key], func(p placeholder) (string, error) {
			return m.resolveWith(p, topology, certificate)
		})
		if err != nil {
			return err
		}
		out[key] = value
	}
	return nil
}

// checkApp refuses a variable the app section sets twice, and placeholders the section cannot resolve: the runner's
// certificate placeholders are allowed in accessTokenSigning only.
func (m *Manifest) checkApp() error {
	seen := map[string]string{}
	claim := func(section, key string) error {
		if other, dup := seen[key]; dup {
			return fmt.Errorf("%w %s in app.%s and app.%s", errAppKey, key, other, section)
		}
		seen[key] = section
		return nil
	}
	sections := []struct {
		values      map[string]string
		name        string
		certificate bool
	}{
		{name: "environment", values: m.App.Environment, certificate: false},
		{name: "fixedEnvironment", values: m.App.FixedEnvironment, certificate: false},
		{name: "accessTokenSigning", values: m.App.AccessTokenSigning, certificate: true},
	}
	for _, section := range sections {
		for _, key := range slices.Sorted(maps.Keys(section.values)) {
			if err := claim(section.name, key); err != nil {
				return err
			}
			if err := m.checkAppPlaceholders(section.values[key], section.certificate); err != nil {
				return fmt.Errorf("app.%s.%s: %w", section.name, key, err)
			}
		}
	}
	if err := claim("secrets.directoryEnv", m.App.Secrets.DirectoryEnv); err != nil {
		return err
	}
	names := map[string]bool{}
	for _, f := range m.App.Secrets.Files {
		if names[f.Name] {
			return fmt.Errorf("%w app secrets file %q", errDuplicate, f.Name)
		}
		names[f.Name] = true
		if err := claim("secrets.files", f.Env); err != nil {
			return err
		}
	}
	return nil
}

func (m *Manifest) checkAppPlaceholders(s string, certificate bool) error {
	var dummy *AppCertificate
	if certificate {
		dummy = &AppCertificate{Path: "", KeysFolder: ""}
	}
	_, err := expand(s, func(p placeholder) (string, error) {
		switch p.name {
		case PlaceholderURL, PlaceholderHost, PlaceholderPlatform:
			return "", nil
		default:
			return m.resolveWith(p, nil, dummy)
		}
	})
	return err
}
