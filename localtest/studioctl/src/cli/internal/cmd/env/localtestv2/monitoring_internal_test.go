package localtestv2

import (
	"slices"
	"testing"
)

var monitoringContainers = []string{ //nolint:gochecknoglobals // A test fixture.
	"victoria-metrics", "victoria-traces", "victoria-logs", "otel-collector", "grafana",
}

func withMonitoring() *Options {
	opts := defaultOptions(testCertDir, testFilesDir)
	opts.Groups = []string{monitoringGroup}
	return opts
}

func TestTheMonitoringGroupStartsTheCollectorItsStoresAndGrafana(t *testing.T) {
	t.Parallel()
	got := selected(withMonitoring())
	for _, name := range monitoringContainers {
		if !slices.Contains(got, name) {
			t.Errorf("--with monitoring leaves out %s", name)
		}
	}
	for _, name := range selected(defaultOptions(testCertDir, testFilesDir)) {
		if slices.Contains(monitoringContainers, name) {
			t.Errorf("the default stack starts %s", name)
		}
	}
	everything, err := ResolveGroups([]string{GroupEverything}, nil)
	if err != nil || !slices.Contains(everything, monitoringGroup) {
		t.Fatalf("everything = %v, %v; want monitoring in it", everything, err)
	}
}

// While the collector runs, every other container gets the standard OpenTelemetry variables, its own name as the
// service name; the monitoring containers get none, so the collector does not export to itself.
func TestTheCollectorGivesEveryOtherContainerTheOTLPVariables(t *testing.T) {
	t.Parallel()
	for name, c := range containersOf(t, withMonitoring()) {
		env := envOf(c)
		if slices.Contains(monitoringContainers, name) {
			for _, key := range []string{otlpEndpointVariable, otlpProtocolVariable, otelServiceNameVariable} {
				if value, ok := env[key]; ok {
					t.Errorf("%s: %s=%s", name, key, value)
				}
			}
			continue
		}
		if got := env[otlpEndpointVariable]; got != "http://otel-collector:4317" {
			t.Errorf("%s: endpoint %q", name, got)
		}
		if got := env[otlpProtocolVariable]; got != "grpc" {
			t.Errorf("%s: protocol %q", name, got)
		}
		if got := env[otelServiceNameVariable]; got != name {
			t.Errorf("%s: service name %q", name, got)
		}
	}
}

func TestWithoutTheCollectorNoContainerGetsTheOTLPVariables(t *testing.T) {
	t.Parallel()
	all := defaultOptions(testCertDir, testFilesDir)
	all.Groups = []string{"dialogporten", "receipt", "correspondence", "arbeidsflate"}
	for _, opts := range []*Options{defaultOptions(testCertDir, testFilesDir), minimalOptions(), all} {
		for name, c := range containersOf(t, opts) {
			for key, value := range envOf(c) {
				if key == otlpEndpointVariable || key == otlpProtocolVariable || key == otelServiceNameVariable ||
					key == "services__otel-collector__http__0" {
					t.Errorf("%s: %s=%s without the collector", name, key, value)
				}
			}
		}
	}
}

// A container's own envs win over the runner's telemetry defaults.
func TestAContainersOwnEnvsWinOverTheOTLPDefaults(t *testing.T) {
	t.Parallel()
	env := containerEnv(
		telemetryEnv([]string{otelCollector, "workflow-engine"}, "workflow-engine"),
		map[string]string{otelServiceNameVariable: "workflow-engine-app"},
		nil,
		nil,
	)
	for _, want := range []string{
		"OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317",
		"OTEL_EXPORTER_OTLP_PROTOCOL=grpc",
		"OTEL_SERVICE_NAME=workflow-engine-app",
	} {
		if !slices.Contains(env, want) {
			t.Errorf("env %v lacks %s", env, want)
		}
	}
	if len(env) != 3 {
		t.Errorf("env %v, want 3 entries", env)
	}
}

// The front door routes /otlp to the collector's OTLP/HTTP receiver, by its services__ variable, although the
// collector's health is a TCP check.
func TestTheCollectorsHTTPReceiverIsAnnounced(t *testing.T) {
	t.Parallel()
	containers := containersOf(t, withMonitoring())
	env := envOf(containers[frontDoor])
	if got := env["services__otel-collector__http__0"]; got != "http://otel-collector:4318" {
		t.Errorf("the collector is %q", got)
	}
	if got := env["services__grafana__http__0"]; got != "http://grafana:3000" {
		t.Errorf("grafana is %q", got)
	}
	if got := healthTargetOf(otelCollector).String(); got != "tcp otel-collector:4318" {
		t.Errorf("the collector's health is %s", got)
	}
	if got := healthTargetOf("grafana").String(); got != "http://grafana:3000/api/health" {
		t.Errorf("grafana's health is %s", got)
	}
}

func TestGrafanaAndTheCollectorMountTheirFiles(t *testing.T) {
	t.Parallel()
	containers := containersOf(t, withMonitoring())
	want := map[string][]string{
		otelCollector: {"/etc/otelcol-contrib/config.yaml"},
		"grafana": {
			"/etc/grafana/provisioning/dashboards/dashboards.yaml",
			"/etc/grafana/provisioning/datasources/datasources.yaml",
			"/var/lib/grafana/dashboards",
		},
	}
	for name, targets := range want {
		var got []string
		for _, v := range containers[name].Volumes {
			if v.ReadOnly && v.HostPath != testCertDir && v.ContainerPath != legacyCertificateFile {
				got = append(got, v.ContainerPath)
			}
		}
		if !slices.Equal(got, targets) {
			t.Errorf("%s mounts %v, want %v", name, got, targets)
		}
	}
	if got := envOf(containers["grafana"])["GF_SERVER_ROOT_URL"]; got != "http://local.altinn.cloud:8000/grafana/" {
		t.Errorf("grafana's root URL is %q", got)
	}
}
