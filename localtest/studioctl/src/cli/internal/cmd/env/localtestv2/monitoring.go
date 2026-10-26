package localtestv2

import (
	"slices"
	"strconv"
)

// Monitoring (env up --with monitoring): the manifest's monitoring group starts the OpenTelemetry collector, the
// Victoria stores behind it and Grafana. While the collector runs, every other container is told where to send its
// telemetry through the standard OpenTelemetry variables; a component exports exactly when they are set.
const (
	// monitoringGroup is the manifest group of the collector, its stores and Grafana.
	monitoringGroup = "monitoring"
	// otelCollector is the collector's manifest name. Its manifest port is the OTLP/HTTP receiver (4318), which the
	// front door's /otlp route reaches through its services__ variable; its health is a TCP connection, since the
	// receiver answers no GET with 200.
	otelCollector = "otel-collector"
	// otelCollectorGRPCPort is the collector's OTLP/gRPC receiver, which the containers send to: inside the network
	// gRPC needs no front door, and some components send over gRPC whatever the protocol variable says.
	otelCollectorGRPCPort = 4317

	otlpEndpointVariable    = "OTEL_EXPORTER_OTLP_ENDPOINT"
	otlpProtocolVariable    = "OTEL_EXPORTER_OTLP_PROTOCOL"
	otelServiceNameVariable = "OTEL_SERVICE_NAME"
	// metricExportIntervalVariable is the SDKs' metric export interval in milliseconds; locally 5 s instead of the
	// specification's 60 s, so a dashboard shows a request within seconds.
	metricExportIntervalVariable = "OTEL_METRIC_EXPORT_INTERVAL"
	metricExportInterval         = "5000"
)

// sendsTelemetry reports whether a container is given the collector: while the collector runs, every container
// outside the monitoring group (the collector, its stores and Grafana, with what they depend on).
func sendsTelemetry(running []string, name string) bool {
	return slices.Contains(running, otelCollector) && !slices.Contains(stack().Select([]string{monitoringGroup}), name)
}

// telemetryEnv is what a container gets when the collector runs: the collector's gRPC address, the protocol, its own
// name as the service name and the metric export interval. The monitoring containers get none, so the collector never
// exports to itself, and nothing is added while the collector is not running. The container's own envs win
// (containerEnv).
func telemetryEnv(running []string, name string) map[string]string {
	if !sendsTelemetry(running, name) {
		return nil
	}
	return map[string]string{
		otlpEndpointVariable:         "http://" + otelCollector + ":" + strconv.Itoa(otelCollectorGRPCPort),
		otlpProtocolVariable:         "grpc",
		otelServiceNameVariable:      name,
		metricExportIntervalVariable: metricExportInterval,
	}
}

// telemetryDependsOn is the runner's dependency of a container that sends telemetry on the collector, besides its
// manifest dependsOn: it starts once the collector is ready, so its first export finds the receiver, and stops before
// the collector, so its last one does too. The collector depends only on the monitoring group's own containers, which
// get no such dependency, so it cannot make a cycle. Its readiness, a TCP connection to the OTLP/HTTP receiver (4318),
// also covers the gRPC receiver the containers use (4317): the collector starts its receivers after its exporters and
// processors, and the OTLP receiver opens its gRPC listener before its HTTP one (otlpReceiver.Start).
func telemetryDependsOn(running []string, name string) []string {
	if !sendsTelemetry(running, name) {
		return nil
	}
	return []string{otelCollector}
}

// announced reports whether a running container gets a services__ variable: every HTTP container, and the collector,
// whose HTTP receiver has no health path of its own.
func announced(name string) bool {
	return stack().Containers[name].IsHTTP() || name == otelCollector
}
