package config

import (
	"maps"
	"os"
	"slices"
	"strings"

	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

// Container images used by the local environment.
//
// Images that mirror a deployed environment track it rather than a pinned build, so a
// change reaching tt02 reaches the local environment without a studioctl release. The
// rest are pinned, having no deployed counterpart to follow.
const (
	imageLocaltest      = "ghcr.io/altinn/altinn-studio/runtime-localtest"
	imagePDF3           = "ghcr.io/altinn/altinn-studio/runtime-pdf3-worker"
	imageWorkflowEngine = "ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app"

	// tagLocaltest follows every localtest build on main.
	tagLocaltest = "latest"

	// tagTT02 follows the build deployed to tt02, moved by the ring-tagging jobs in
	// .github/workflows/deploy-runtime-{pdf3,workflow-engine-app}.yaml when the tt_ring1
	// runtime ring, which is what tt02 serves, is tagged.
	tagTT02 = "tt02"

	// tagLatest follows every build on main.
	tagLatest = "latest"
)

// ImageSpec defines an image reference with repository and tag.
type ImageSpec struct {
	Image string
	Tag   string

	// Floating marks a tag that moves as new builds are published. Such an image is
	// re-pulled whenever the environment starts, keeping the local copy when the registry
	// is unreachable.
	Floating bool
}

// Ref returns the full image reference (image:tag).
func (s ImageSpec) Ref() string {
	if s.Tag == "" {
		return s.Image + ":latest"
	}
	return s.Image + ":" + s.Tag
}

// CoreImages holds image configuration for core studioctl containers.
type CoreImages struct {
	Localtest        ImageSpec
	PDF3             ImageSpec
	WorkflowEngineDb ImageSpec
	WorkflowEngine   ImageSpec
	PgAdmin          ImageSpec
}

// LocaltestV2Images holds the images of the localtest-v2 runtime by image name, as the component manifest
// (internal/localtestv2/manifest) lists them: the research builds, one per component plus the localtest app and the
// shared Postgres, pinned to the manifest's tag, and the third-party images (Redis), pinned by digest. The PDF
// generator and the workflow engine are the ones the localtest runtime runs (CoreImages).
//
// The specs are shared between copies of the value until UseTag, which copies them first.
type LocaltestV2Images struct {
	specs    map[string]ImageSpec
	research []string
}

// Image returns the named image.
func (i LocaltestV2Images) Image(name string) (ImageSpec, bool) {
	spec, ok := i.specs[name]
	return spec, ok
}

// Names lists every image, sorted.
func (i LocaltestV2Images) Names() []string {
	return slices.Sorted(maps.Keys(i.specs))
}

// ResearchNames lists the images built by the research repository, which one tag applies to, in manifest order.
func (i LocaltestV2Images) ResearchNames() []string {
	return slices.Clone(i.research)
}

// UseTag runs every research image at one tag. "latest" moves with main, so it is re-pulled on every start; any other
// tag names one build. Third-party images keep their pins.
func (i *LocaltestV2Images) UseTag(tag string) {
	i.specs = maps.Clone(i.specs)
	for _, name := range i.research {
		spec := i.specs[name]
		spec.Tag = tag
		spec.Floating = tag == tagLatest
		i.specs[name] = spec
	}
}

// EnvLocaltestV2Tag is the variable that overrides the tag every localtest-v2 research image runs ("latest" follows
// main), from the manifest.
func EnvLocaltestV2Tag() string {
	return manifest.MustDefault().Images.TagEnv
}

// LocaltestV2OverrideEnv is the variable that overrides one localtest-v2 image with a complete reference:
// STUDIOCTL_IMAGE_LOCALTEST_V2_ plus the image name upper-cased with - turned into _.
func LocaltestV2OverrideEnv(name string) string {
	return manifest.MustDefault().Images.OverrideEnv(name)
}

func defaultLocaltestV2Images(m *manifest.Manifest) LocaltestV2Images {
	images := LocaltestV2Images{specs: map[string]ImageSpec{}, research: m.ResearchNames()}
	for _, name := range images.research {
		images.specs[name] = ImageSpec{Image: m.Images.RepositoryPrefix + name, Tag: m.Images.Tag, Floating: false}
	}
	for name, external := range m.Images.ThirdParty {
		images.specs[name] = ImageSpec{Image: external.Repository, Tag: external.Tag, Floating: external.Floating}
	}
	return images
}

// MonitoringImages holds image configuration for monitoring stack containers.
type MonitoringImages struct {
	VictoriaMetrics ImageSpec
	VictoriaTraces  ImageSpec
	VictoriaLogs    ImageSpec
	OtelCollector   ImageSpec
	Grafana         ImageSpec
}

// ImagesConfig holds all image configuration grouped by purpose.
type ImagesConfig struct {
	Core        CoreImages
	Monitoring  MonitoringImages
	LocaltestV2 LocaltestV2Images
}

// DefaultImages returns the images the local environment runs, with any
// STUDIOCTL_IMAGE_* override applied.
func DefaultImages() ImagesConfig {
	images := ImagesConfig{
		Core: CoreImages{
			Localtest:        ImageSpec{Image: imageLocaltest, Tag: tagLocaltest, Floating: true},
			PDF3:             ImageSpec{Image: imagePDF3, Tag: tagTT02, Floating: true},
			WorkflowEngine:   ImageSpec{Image: imageWorkflowEngine, Tag: tagTT02, Floating: true},
			WorkflowEngineDb: ImageSpec{Image: "postgres", Tag: "18.3", Floating: false},
			PgAdmin:          ImageSpec{Image: "dpage/pgadmin4", Tag: "9.14", Floating: false},
		},
		Monitoring: MonitoringImages{
			VictoriaMetrics: ImageSpec{Image: "victoriametrics/victoria-metrics", Tag: "v1.153.0", Floating: false},
			VictoriaTraces:  ImageSpec{Image: "victoriametrics/victoria-traces", Tag: "v0.12.0", Floating: false},
			VictoriaLogs:    ImageSpec{Image: "victoriametrics/victoria-logs", Tag: "v1.52.0", Floating: false},
			OtelCollector:   ImageSpec{Image: "otel/opentelemetry-collector-contrib", Tag: "0.161.0", Floating: false},
			Grafana:         ImageSpec{Image: "grafana/grafana", Tag: "12.4.8", Floating: false},
		},
		LocaltestV2: defaultLocaltestV2Images(manifest.MustDefault()),
	}
	if tag := strings.TrimSpace(os.Getenv(EnvLocaltestV2Tag())); tag != "" {
		images.LocaltestV2.UseTag(tag)
	}
	images.applyEnvOverrides(os.Getenv)
	return images
}

// imageOverrides maps each image to its environment variable, which holds a complete
// reference ("repository:tag"; an untagged one resolves to ":latest"). An override names one
// build to run, so it also stops that image from following a tag.
func (c *ImagesConfig) imageOverrides() map[string]*ImageSpec {
	return map[string]*ImageSpec{
		"STUDIOCTL_IMAGE_LOCALTEST":          &c.Core.Localtest,
		"STUDIOCTL_IMAGE_PDF3":               &c.Core.PDF3,
		"STUDIOCTL_IMAGE_WORKFLOW_ENGINE":    &c.Core.WorkflowEngine,
		"STUDIOCTL_IMAGE_WORKFLOW_ENGINE_DB": &c.Core.WorkflowEngineDb,
		"STUDIOCTL_IMAGE_PGADMIN":            &c.Core.PgAdmin,
		"STUDIOCTL_IMAGE_VICTORIA_METRICS":   &c.Monitoring.VictoriaMetrics,
		"STUDIOCTL_IMAGE_VICTORIA_TRACES":    &c.Monitoring.VictoriaTraces,
		"STUDIOCTL_IMAGE_VICTORIA_LOGS":      &c.Monitoring.VictoriaLogs,
		"STUDIOCTL_IMAGE_OTEL_COLLECTOR":     &c.Monitoring.OtelCollector,
		"STUDIOCTL_IMAGE_GRAFANA":            &c.Monitoring.Grafana,
	}
}

func (c *ImagesConfig) applyEnvOverrides(getenv func(string) string) {
	for envVar, spec := range c.imageOverrides() {
		if ref := strings.TrimSpace(getenv(envVar)); ref != "" {
			*spec = overriddenSpec(ref)
		}
	}
	// The localtest-v2 images' variables follow a rule from the manifest (LocaltestV2OverrideEnv).
	for _, name := range c.LocaltestV2.Names() {
		if ref := strings.TrimSpace(getenv(LocaltestV2OverrideEnv(name))); ref != "" {
			c.LocaltestV2.specs[name] = overriddenSpec(ref)
		}
	}
}

// overriddenSpec is the spec of an override reference: it names one build, so it does not follow a tag.
func overriddenSpec(ref string) ImageSpec {
	image, tag := splitImageRef(ref)
	return ImageSpec{Image: image, Tag: tag, Floating: false}
}

// splitImageRef splits "repository:tag". A registry host may carry a port, so only a colon
// after the last path separator delimits the tag.
func splitImageRef(ref string) (image, tag string) {
	lastColon := strings.LastIndex(ref, ":")
	if lastColon < 0 || strings.Contains(ref[lastColon+1:], "/") {
		return ref, ""
	}
	return ref[:lastColon], ref[lastColon+1:]
}

// shortImageIDLength keeps an image ID recognizable while staying short enough to render inline.
const shortImageIDLength = 12

// ShortImageID abbreviates a container runtime image ID to its leading hex characters. Runtimes
// report it either bare or as "sha256:<hex>".
func ShortImageID(imageID string) string {
	hex := imageID
	if _, rest, found := strings.Cut(imageID, ":"); found {
		hex = rest
	}
	if len(hex) > shortImageIDLength {
		return hex[:shortImageIDLength]
	}
	return hex
}
