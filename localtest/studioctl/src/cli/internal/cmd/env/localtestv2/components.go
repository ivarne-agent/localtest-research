package localtestv2

import (
	"errors"
	"fmt"
	"path/filepath"
	"slices"
	"strconv"

	"altinn.studio/devenv/pkg/container/types"
	"altinn.studio/devenv/pkg/resource"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	statev2 "altinn.studio/studioctl/internal/localtestv2"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

// The components, their groups, ports, environment, mounts, dependencies and routes are defined by the component
// manifest (internal/localtestv2/manifest/manifest.json); this file loads it into the values the runtime starts.

const (
	// Containers reach the public origin, served by studioctl-server on the host, through the runtime's host
	// gateway: every ingress hostname resolves to it (the PDF generator's browser loads the app's frontend assets
	// from app-frontend.local.altinn.cloud when `studioctl run --dev-frontend` serves them), and so does each of a
	// component's extraHostGatewayNames.
	hostGateway = "host-gateway"

	// loopback is where every host port is published; the front door and the health wait reach the components there.
	loopback = "127.0.0.1"
)

var (
	errManifestImage    = errors.New("manifest component image unknown to the image configuration")
	errManifestTopology = errors.New("manifest names a topology component studioctl does not know")
)

// stack is the embedded component manifest. It is checked by its package's tests, so an invalid one never ships.
func stack() *manifest.Manifest {
	return manifest.MustDefault()
}

// containerOf is the container name of a component.
func containerOf(name string) string {
	return stack().ContainerPrefix + name
}

// route is one public host-and-prefix the proxy forwards to a component; an empty prefix matches the whole host.
// rewrite, when set, is the path the proxy forwards in place of the matched prefix (the rest of the path is kept), for a
// component that serves itself under another path than the public one.
type route struct {
	host    string
	prefix  string
	rewrite string
}

// component is one container of the environment and how the proxy and the health wait reach it.
type component struct {
	healthCheck *types.HealthCheck
	env         map[string]string
	name        string
	// group is the optional component group the component belongs to; empty for the always-on ones.
	group string
	image config.ImageSpec
	// devSelector is the `--dev` name that builds the component; empty when it has no dev build (the PDF generator
	// and the workflow engine come from the monorepo).
	devSelector string
	// devContext is the build context under the research checkout for a dev build.
	devContext string
	// dockerfile is the dev build's Dockerfile, relative to devContext.
	dockerfile string
	// command overrides the image's command.
	command       []string
	containerPort string
	hostPort      string
	// healthPath is polled on hostPort over HTTP after the containers run; empty skips the poll.
	healthPath     string
	volumes        []types.VolumeMount
	extraHosts     []string
	aliases        []string
	deps           []string
	routes         []route
	useDefaultUser bool
}

func (c component) containerName() string {
	return containerOf(c.name)
}

func (c component) hostURL() string {
	return "http://" + loopback + ":" + c.hostPort
}

// Options configures the component set.
type Options struct {
	DevComponents map[string]bool
	Groups        map[string]bool
	CertDir       string
	DevSourceRoot string
	RuntimeUser   string
	UsernsMode    string
	Images        config.ImagesConfig
	Topology      envtopology.Local
	RelabelBinds  bool
}

// ingressHostEntries maps every public hostname to the host, where studioctl-server serves them.
func ingressHostEntries(opts *Options) []string {
	hosts := opts.Topology.LocaltestIngressHosts()
	entries := make([]string, 0, len(hosts))
	for _, host := range hosts {
		entries = append(entries, host+":"+hostGateway)
	}
	return entries
}

// components returns the environment's containers in start order: the always-on ones, then those of the enabled groups.
func components(opts *Options) []component {
	var out []component
	for _, c := range allComponents(opts) {
		if c.group == "" || opts.Groups[c.group] {
			out = append(out, c)
		}
	}
	return out
}

// allComponents is every component, whether or not its group is enabled; teardown uses it so that nothing a previous
// start left behind is orphaned. A manifest that does not resolve against the image configuration and the topology is a
// build defect the tests catch, so it panics.
func allComponents(opts *Options) []component {
	comps, err := loadComponents(stack(), opts)
	if err != nil {
		panic(fmt.Sprintf("localtest-v2 component manifest: %v", err))
	}
	return comps
}

// loadComponents resolves the manifest's components against the options: images, topology placeholders, the host
// certificate directory.
func loadComponents(m *manifest.Manifest, opts *Options) ([]component, error) {
	topology := manifestTopology{Local: opts.Topology}
	out := make([]component, 0, len(m.Components))
	for _, mc := range m.Components {
		c, err := loadComponent(m, mc, opts, topology)
		if err != nil {
			return nil, fmt.Errorf("component %s: %w", mc.Name, err)
		}
		out = append(out, c)
	}
	return out, nil
}

func loadComponent(
	m *manifest.Manifest,
	mc manifest.Component,
	opts *Options,
	topology manifestTopology,
) (component, error) {
	image, err := componentSpec(opts, mc.Image)
	if err != nil {
		return component{}, err
	}
	env, err := expandEnvironment(m, mc.Environment, topology)
	if err != nil {
		return component{}, err
	}
	command, err := m.ExpandAll(mc.Command, topology)
	if err != nil {
		return component{}, fmt.Errorf("command: %w", err)
	}
	var healthCheck *types.HealthCheck
	if mc.ContainerHealthCheck != nil {
		if healthCheck, err = containerHealthCheck(m, *mc.ContainerHealthCheck, topology); err != nil {
			return component{}, err
		}
	}
	routes, err := componentRoutes(m, mc.Routes, topology)
	if err != nil {
		return component{}, err
	}
	var devContext, dockerfile string
	if mc.Dev != nil {
		devContext, dockerfile = mc.Dev.Context, mc.Dev.Dockerfile
		if dockerfile == "" {
			dockerfile = manifest.DefaultDockerfile
		}
	}
	return component{
		healthCheck:    healthCheck,
		env:            env,
		name:           mc.Name,
		group:          mc.Group,
		image:          image,
		devSelector:    mc.DevSelector(),
		devContext:     devContext,
		dockerfile:     dockerfile,
		command:        command,
		containerPort:  strconv.Itoa(mc.ContainerPort),
		hostPort:       portString(mc.HostPort),
		healthPath:     mc.HealthPath,
		volumes:        componentVolumes(m, mc.Mounts, opts),
		extraHosts:     extraHosts(mc, opts),
		aliases:        mc.NetworkAliases,
		deps:           componentDeps(mc.DependsOn),
		routes:         routes,
		useDefaultUser: !mc.HostUser,
	}, nil
}

// componentSpec is the image a component runs: a research build or a third-party image from the localtest-v2 images,
// or one the localtest runtime runs too.
func componentSpec(opts *Options, source manifest.ImageSource) (config.ImageSpec, error) {
	if source.Core != "" {
		spec, ok := opts.Images.Core.ByName(source.Core)
		if !ok {
			return config.ImageSpec{}, fmt.Errorf("%w: core %q", errManifestImage, source.Core)
		}
		return spec, nil
	}
	name := source.Research + source.ThirdParty
	spec, ok := opts.Images.LocaltestV2.Image(name)
	if !ok {
		return config.ImageSpec{}, fmt.Errorf("%w: %q", errManifestImage, name)
	}
	return spec, nil
}

func expandEnvironment(
	m *manifest.Manifest,
	values map[string]string,
	topology manifestTopology,
) (map[string]string, error) {
	env := make(map[string]string, len(values))
	for key, value := range values {
		expanded, err := m.Expand(value, topology)
		if err != nil {
			return nil, fmt.Errorf("environment %s: %w", key, err)
		}
		env[key] = expanded
	}
	return env, nil
}

func containerHealthCheck(
	m *manifest.Manifest,
	check manifest.HealthCheck,
	topology manifestTopology,
) (*types.HealthCheck, error) {
	test, err := m.ExpandAll(check.Test, topology)
	if err != nil {
		return nil, fmt.Errorf("containerHealthCheck: %w", err)
	}
	interval, timeout, startPeriod, err := check.Durations()
	if err != nil {
		return nil, fmt.Errorf("containerHealthCheck: %w", err)
	}
	return &types.HealthCheck{
		Test:        test,
		Interval:    interval,
		Timeout:     timeout,
		Retries:     check.Retries,
		StartPeriod: startPeriod,
	}, nil
}

func componentRoutes(m *manifest.Manifest, routes []manifest.Route, topology manifestTopology) ([]route, error) {
	out := make([]route, 0, len(routes))
	for _, r := range routes {
		host, err := m.Expand(r.Host, topology)
		if err != nil {
			return nil, fmt.Errorf("route: %w", err)
		}
		out = append(out, route{host: host, prefix: r.Prefix, rewrite: r.Rewrite})
	}
	return out, nil
}

// componentVolumes are the certificate binds (read-only, from the host's certificate directory) and named volumes.
func componentVolumes(m *manifest.Manifest, mounts []manifest.Mount, opts *Options) []types.VolumeMount {
	if mounts == nil {
		return nil
	}
	volumes := make([]types.VolumeMount, 0, len(mounts))
	for _, mount := range mounts {
		switch mount.Kind {
		case manifest.MountCertificateDir:
			volumes = append(volumes, readOnlyBind(opts.CertDir, m.Certificate.ContainerDir))
		case manifest.MountCertificateFile:
			volumes = append(volumes, readOnlyBind(filepath.Join(opts.CertDir, m.Certificate.FileName), mount.Target))
		case manifest.MountVolume:
			volumes = append(volumes, namedVolume(mount.Volume, mount.Target))
		}
	}
	return volumes
}

// extraHosts resolves the public hostnames, and any extra names the component asks for, to the host gateway.
func extraHosts(mc manifest.Component, opts *Options) []string {
	var hosts []string
	if mc.MapsIngressHosts() {
		hosts = ingressHostEntries(opts)
	}
	for _, name := range mc.ExtraHostGatewayNames {
		hosts = append(hosts, name+":"+hostGateway)
	}
	return hosts
}

func componentDeps(names []string) []string {
	if names == nil {
		return nil
	}
	deps := make([]string, 0, len(names))
	for _, name := range names {
		deps = append(deps, containerOf(name))
	}
	return deps
}

func portString(port int) string {
	if port == 0 {
		return ""
	}
	return strconv.Itoa(port)
}

// manifestTopology resolves the manifest's topology placeholders ({{url:...}}, {{host:...}}) against envtopology.
type manifestTopology = statev2.ManifestTopology

// expandTopology resolves a string that may only hold topology placeholders, such as a front-door host route's host.
func expandTopology(opts *Options, s string) string {
	expanded, err := stack().Expand(s, manifestTopology{Local: opts.Topology})
	if err != nil {
		panic(fmt.Sprintf("localtest-v2 component manifest: %v: %v", errManifestTopology, err))
	}
	return expanded
}

// DevBuildable lists the components a dev build can produce from the research checkout, by their `--dev` names: a
// group's name selects the components built together from the group's folder (Dialogporten's three hosts, the two
// Arbeidsflate images).
func DevBuildable() []string {
	var names []string
	for _, c := range stack().Components {
		if selector := c.DevSelector(); selector != "" && !slices.Contains(names, selector) {
			names = append(names, selector)
		}
	}
	return names
}

func defaultOptions(images config.ImagesConfig, certDir string) *Options {
	return &Options{
		DevComponents: nil,
		Images:        images,
		Topology:      envtopology.NewLocal(envtopology.DefaultIngressPortString()),
		Groups:        nil,
		CertDir:       certDir,
		DevSourceRoot: "",
		RuntimeUser:   "",
		UsernsMode:    "",
		RelabelBinds:  false,
	}
}

func readOnlyBind(hostPath, containerPath string) types.VolumeMount {
	return types.VolumeMount{
		HostPath:       hostPath,
		ContainerPath:  containerPath,
		Type:           types.VolumeMountTypeBind,
		SELinuxRelabel: types.SELinuxRelabelNone,
		ReadOnly:       true,
	}
}

func namedVolume(name, containerPath string) types.VolumeMount {
	return types.VolumeMount{
		HostPath:       name,
		ContainerPath:  containerPath,
		Type:           types.VolumeMountTypeVolume,
		SELinuxRelabel: types.SELinuxRelabelNone,
		ReadOnly:       false,
	}
}

// resources turns the components into the devenv resource graph: the network, then per component an image
// (pulled, or built from the research checkout) and a container.
func resources(opts *Options) []resource.Resource {
	network := &resource.Network{
		Enabled: nil,
		Name:    stack().Network,
		Driver:  "bridge",
		Labels:  nil,
		Lifecycle: resource.LifecycleOptions{
			HandleDestroyError: nil,
			RetainOnDestroy:    false,
		},
	}
	comps := components(opts)
	out := make([]resource.Resource, 0, 1+2*len(comps))
	out = append(out, network)
	for _, c := range comps {
		image := componentImage(opts, c)
		out = append(out, image, componentContainer(opts, c, image, resource.Ref(network)))
	}
	return out
}

func componentImage(opts *Options, c component) resource.ImageResource {
	if c.devSelector != "" && opts.DevComponents[c.devSelector] && opts.DevSourceRoot != "" {
		buildContext := filepath.Join(opts.DevSourceRoot, c.devContext)
		return &resource.BuiltImage{
			Enabled:     nil,
			ContextPath: filepath.ToSlash(buildContext),
			Dockerfile:  filepath.ToSlash(filepath.Join(buildContext, c.dockerfile)),
			Build:       types.BuildOptions{CacheFrom: nil, CacheTo: nil},
			Tag:         c.containerName() + ":dev",
		}
	}
	policy := resource.PullIfNotPresent
	if c.image.Floating {
		policy = resource.PullAlwaysAllowStale
	}
	return &resource.PulledImage{Enabled: nil, Ref: c.image.Ref(), PullPolicy: policy}
}

func componentContainer(
	opts *Options,
	c component,
	image resource.ImageResource,
	network resource.ResourceRef,
) *resource.Container {
	var ports []types.PortMapping
	if c.hostPort != "" {
		ports = []types.PortMapping{{
			HostPort:      c.hostPort,
			ContainerPort: c.containerPort,
			HostIP:        loopback,
			Protocol:      "",
		}}
	}
	deps := make([]resource.ResourceRef, 0, len(c.deps))
	for _, dep := range c.deps {
		deps = append(deps, resource.RefID(resource.ContainerID(dep)))
	}
	user, usernsMode := opts.RuntimeUser, opts.UsernsMode
	if c.useDefaultUser {
		user, usernsMode = "", ""
	}
	volumes := make([]types.VolumeMount, 0, len(c.volumes))
	for _, volume := range c.volumes {
		if opts.RelabelBinds && volume.Type == types.VolumeMountTypeBind {
			volume.SELinuxRelabel = types.SELinuxRelabelShared
		}
		volumes = append(volumes, volume)
	}
	env := make([]string, 0, len(c.env))
	for key, value := range c.env {
		env = append(env, key+"="+value)
	}
	slices.Sort(env)

	return &resource.Container{
		HealthCheck: c.healthCheck,
		Name:        c.containerName(),
		Image:       resource.Ref(image),
		Enabled:     nil,
		Networks:    []resource.ResourceRef{network},
		DependsOn:   deps,
		Ports:       ports,
		Volumes:     volumes,
		Env:         env,
		Labels:      nil,
		Command:     c.command,
		ExtraHosts:  c.extraHosts,
		Lifecycle: resource.ContainerLifecycleOptions{
			LifecycleOptions: resource.LifecycleOptions{
				HandleDestroyError: nil,
				RetainOnDestroy:    false,
			},
			WaitForReady: true,
		},
		NetworkAliases: c.aliases,
		RestartPolicy:  "",
		User:           user,
		UsernsMode:     usernsMode,
	}
}

// containerNames lists the containers in start order, for logs and status.
func containerNames(opts *Options) []string {
	comps := components(opts)
	names := make([]string, 0, len(comps))
	for _, c := range comps {
		names = append(names, c.containerName())
	}
	return names
}
