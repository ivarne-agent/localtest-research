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
)

// telemetryEnv is what a container gets when the collector runs: the collector's gRPC address, the protocol and its
// own name as the service name. The monitoring containers get none, so the collector never exports to itself, and
// nothing is added while the collector is not running. The container's own envs win (containerEnv).
func telemetryEnv(running []string, name string) map[string]string {
	if !slices.Contains(running, otelCollector) || slices.Contains(stack().Select([]string{monitoringGroup}), name) {
		return nil
	}
	return map[string]string{
		otlpEndpointVariable:    "http://" + otelCollector + ":" + strconv.Itoa(otelCollectorGRPCPort),
		otlpProtocolVariable:    "grpc",
		otelServiceNameVariable: name,
	}
}

// announced reports whether a running container gets a services__ variable: every HTTP container, and the collector,
// whose HTTP receiver has no health path of its own.
func announced(name string) bool {
	return stack().Containers[name].IsHTTP() || name == otelCollector
}
