package localtestv2

import "altinn.studio/studioctl/internal/envtopology"

// frontendDevServerComponent is the route for `studioctl run --dev-frontend`: the Vite dev server on the host.
const frontendDevServerComponent = "frontendDevServer"

// boundTopologyConfig is what studioctl-server proxies: every component route points at the component's
// loopback-published port, and the server adds /{org}/{app} for each app it discovers. The prefixes are
// matched on the public host; studioctl-server falls back to that host for a request addressed to
// localhost or 127.0.0.1, so app-lib's PlatformSettings defaults and its own storage probe reach the
// components too. The routes of an optional group that is off are written too, disabled and with the group in
// their metadata, so that the proxy answers 503 with the command that starts the group instead of passing the
// request on to the localtest app.
func boundTopologyConfig(opts *Options) envtopology.BoundTopologyConfig {
	var routes []envtopology.BoundTopologyRoute
	for _, c := range allComponents(opts) {
		enabled := c.group == "" || opts.Groups[c.group]
		for _, r := range c.routes {
			routes = append(routes, hostRoute(c.name, r.host, r.prefix, c.hostURL(), r.rewrite, c.group, enabled))
		}
	}
	routes = append(routes, hostRoute(
		frontendDevServerComponent,
		opts.Topology.FrontendDevServerHostName(),
		"",
		"http://"+loopback+":"+frontendDevServerHostPort,
		"",
		"",
		true,
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

// groupMetadataKey is the route metadata naming the optional component group of the route's component
// (Altinn.Studio.EnvTopology.BoundTopologyMetadataKeys.Group); the proxy names it when the group is off.
const groupMetadataKey = "group"

func hostRoute(component, host, prefix, url, rewrite, group string, enabled bool) envtopology.BoundTopologyRoute {
	var metadata []envtopology.BoundTopologyMetadata
	if rewrite != "" {
		metadata = append(metadata, envtopology.BoundTopologyMetadata{Key: rewriteMetadataKey, Value: rewrite})
	}
	if group != "" {
		metadata = append(metadata, envtopology.BoundTopologyMetadata{Key: groupMetadataKey, Value: group})
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
		Enabled:   enabled,
	}
}
