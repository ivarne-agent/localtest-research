package localtestv2

import "altinn.studio/studioctl/internal/envtopology"

// frontendDevServerComponent is the route for `studioctl run --dev-frontend`: the Vite dev server on the host.
const frontendDevServerComponent = "frontendDevServer"

// boundTopologyConfig is what studioctl-server proxies: every component route points at the component's
// loopback-published port, and the server adds /{org}/{app} for each app it discovers. The prefixes are
// matched on the public host; studioctl-server falls back to that host for a request addressed to
// localhost or 127.0.0.1, so app-lib's PlatformSettings defaults and its own storage probe reach the
// components too.
func boundTopologyConfig(opts *Options) envtopology.BoundTopologyConfig {
	var routes []envtopology.BoundTopologyRoute
	for _, c := range components(opts) {
		for _, r := range c.routes {
			routes = append(routes, hostRoute(c.name, r.host, r.prefix, c.hostURL(), r.rewrite))
		}
	}
	routes = append(routes, hostRoute(
		frontendDevServerComponent,
		opts.Topology.FrontendDevServerHostName(),
		"",
		"http://"+loopback+":"+frontendDevServerHostPort,
		"",
	))

	return envtopology.BoundTopologyConfig{
		AppRouteTemplate: envtopology.BoundTopologyAppRouteTemplate{
			Host:               opts.Topology.AppHostName(),
			PathPrefixTemplate: "/{org}/{app}",
		},
		Routes:  routes,
		Version: envtopology.BoundTopologyConfigVersion,
	}
}

// rewriteMetadataKey is the route metadata studioctl-server's proxy reads as the path to forward in place of the
// route's prefix (Altinn.Studio.EnvTopology.BoundTopologyMetadataKeys.RewritePrefix).
const rewriteMetadataKey = "rewritePrefix"

func hostRoute(component, host, prefix, url, rewrite string) envtopology.BoundTopologyRoute {
	var metadata []envtopology.BoundTopologyMetadata
	if rewrite != "" {
		metadata = []envtopology.BoundTopologyMetadata{{Key: rewriteMetadataKey, Value: rewrite}}
	}
	return envtopology.BoundTopologyRoute{
		Destination: envtopology.BoundTopologyDestination{
			Location: envtopology.DestinationLocationHost,
			Kind:     envtopology.DestinationKindHTTP,
			URL:      url,
		},
		Match:     envtopology.BoundTopologyRouteMatch{Host: host, PathPrefix: prefix},
		Component: envtopology.ComponentID(component),
		Metadata:  metadata,
		Enabled:   true,
	}
}
