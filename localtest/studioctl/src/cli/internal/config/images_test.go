package config_test

import (
	"maps"
	"regexp"
	"slices"
	"strings"
	"testing"

	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/localtestv2/manifest"
)

func TestDefaultImages(t *testing.T) {
	t.Parallel()

	images := config.DefaultImages()

	t.Run("images mirroring a deployed environment float", func(t *testing.T) {
		t.Parallel()

		if !images.Core.Localtest.Floating {
			t.Error("localtest image should follow its latest build")
		}
		if !images.Core.WorkflowEngine.Floating {
			t.Error("workflow-engine image should follow the build deployed to tt02")
		}
		if !images.Core.PDF3.Floating {
			t.Error("pdf3 image should follow the build deployed to tt02")
		}
		if got := images.Core.PDF3.Ref(); got != "ghcr.io/altinn/altinn-studio/runtime-pdf3-worker:tt02" {
			t.Errorf("pdf3 ref = %q", got)
		}
		if got := images.Core.WorkflowEngine.Ref(); got !=
			"ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:tt02" {
			t.Errorf("workflow-engine ref = %q", got)
		}
		if got := images.Core.Localtest.Ref(); got != "ghcr.io/altinn/altinn-studio/runtime-localtest:latest" {
			t.Errorf("localtest ref = %q", got)
		}
	})

	t.Run("images without a deployed counterpart stay pinned", func(t *testing.T) {
		t.Parallel()

		pinned := map[string]config.ImageSpec{
			"workflow-engine-db": images.Core.WorkflowEngineDb,
			"pgadmin":            images.Core.PgAdmin,
			"victoria-metrics":   images.Monitoring.VictoriaMetrics,
			"victoria-traces":    images.Monitoring.VictoriaTraces,
			"victoria-logs":      images.Monitoring.VictoriaLogs,
			"otel-collector":     images.Monitoring.OtelCollector,
			"grafana":            images.Monitoring.Grafana,
		}
		for name, spec := range pinned {
			if spec.Floating {
				t.Errorf("%s image should be pinned", name)
			}
			if spec.Tag == "" {
				t.Errorf("%s image should have an explicit tag", name)
			}
		}
	})
}

func TestDefaultImagesEnvOverride(t *testing.T) {
	t.Setenv("STUDIOCTL_IMAGE_WORKFLOW_ENGINE", "ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:a45a743b78")
	t.Setenv("STUDIOCTL_IMAGE_PGADMIN", "localhost:5000/pgadmin4:testing")
	t.Setenv("STUDIOCTL_IMAGE_VICTORIA_TRACES", "  ")

	images := config.DefaultImages()

	if got := images.Core.WorkflowEngine.Ref(); got !=
		"ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:a45a743b78" {
		t.Errorf("workflow-engine ref = %q", got)
	}
	if images.Core.WorkflowEngine.Floating {
		t.Error("an override names one build, so the image should stop following a tag")
	}
	if got := images.Core.PgAdmin.Ref(); got != "localhost:5000/pgadmin4:testing" {
		t.Errorf("pgadmin ref = %q, a registry port should not be read as a tag", got)
	}
	if got := images.Monitoring.VictoriaTraces.Ref(); got != "victoriametrics/victoria-traces:v0.12.0" {
		t.Errorf("victoria-traces ref = %q, a blank override should be ignored", got)
	}
}

func TestImageSpecRef(t *testing.T) {
	t.Parallel()

	tests := []struct {
		name string
		want string
		spec config.ImageSpec
	}{
		{name: "image and tag", want: "postgres:18.3", spec: config.ImageSpec{Image: "postgres", Tag: "18.3"}},
		{name: "no tag defaults to latest", want: "postgres:latest", spec: config.ImageSpec{Image: "postgres"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()
			if got := tt.spec.Ref(); got != tt.want {
				t.Errorf("Ref() = %q, want %q", got, tt.want)
			}
		})
	}
}

func TestShortImageID(t *testing.T) {
	t.Parallel()

	tests := []struct {
		name    string
		imageID string
		want    string
	}{
		{name: "empty", imageID: "", want: ""},
		{
			name:    "algorithm prefix is dropped",
			imageID: "sha256:0123456789abcdef0123456789abcdef",
			want:    "0123456789ab",
		},
		{name: "short id is kept whole", imageID: "sha256:0123", want: "0123"},
		{name: "bare hex", imageID: "0123456789abcdef", want: "0123456789ab"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()
			if got := config.ShortImageID(tt.imageID); got != tt.want {
				t.Errorf("ShortImageID(%q) = %q, want %q", tt.imageID, got, tt.want)
			}
		})
	}
}

func localtestV2Image(t *testing.T, images config.LocaltestV2Images, name string) config.ImageSpec {
	t.Helper()
	spec, ok := images.Image(name)
	if !ok {
		t.Fatalf("no localtest-v2 image %q", name)
	}
	return spec
}

func TestLocaltestV2ImagesArePinnedUnlessTold(t *testing.T) {
	t.Parallel()
	images := config.DefaultImages()
	for _, name := range images.LocaltestV2.ResearchNames() {
		if spec := localtestV2Image(t, images.LocaltestV2, name); spec.Floating || spec.Tag == "latest" {
			t.Errorf("%s: pinned by default, got tag %q floating %v", spec.Image, spec.Tag, spec.Floating)
		}
	}
	latest := images.LocaltestV2
	latest.UseTag("latest")
	for _, name := range latest.ResearchNames() {
		if spec := localtestV2Image(t, latest, name); !spec.Floating || spec.Tag != "latest" {
			t.Errorf("%s: latest must float, got tag %q floating %v", spec.Image, spec.Tag, spec.Floating)
		}
	}
	if localtestV2Image(t, images.LocaltestV2, "storage").Floating {
		t.Error("UseTag changed the configuration it was copied from")
	}
	named := images.LocaltestV2
	named.UseTag("sha-abc1234")
	if spec := localtestV2Image(t, named, "storage"); spec.Floating || spec.Tag != "sha-abc1234" {
		t.Errorf("a named build does not float: %+v", spec)
	}
}

// TestLocaltestV2ImagesAreThoseOfTheHandWrittenTable pins the images and override variables to what images.go listed by
// hand before the component manifest, so the manifest changes neither.
func TestLocaltestV2ImagesAreThoseOfTheHandWrittenTable(t *testing.T) {
	t.Parallel()
	research := []string{
		"localtest", "register", "authentication", "authorization", "access-management", "storage", "events",
		"resource-registry", "notifications", "profile", "postgres", "dialogporten-webapi", "dialogporten-graphql",
		"dialogporten-service", "dialogporten-adapter", "receipt", "correspondence", "arbeidsflate", "arbeidsflate-bff",
	}
	envs := map[string]string{
		"localtest":            "STUDIOCTL_IMAGE_LOCALTEST_V2_LOCALTEST",
		"register":             "STUDIOCTL_IMAGE_LOCALTEST_V2_REGISTER",
		"authentication":       "STUDIOCTL_IMAGE_LOCALTEST_V2_AUTHENTICATION",
		"authorization":        "STUDIOCTL_IMAGE_LOCALTEST_V2_AUTHORIZATION",
		"access-management":    "STUDIOCTL_IMAGE_LOCALTEST_V2_ACCESS_MANAGEMENT",
		"storage":              "STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE",
		"events":               "STUDIOCTL_IMAGE_LOCALTEST_V2_EVENTS",
		"resource-registry":    "STUDIOCTL_IMAGE_LOCALTEST_V2_RESOURCE_REGISTRY",
		"notifications":        "STUDIOCTL_IMAGE_LOCALTEST_V2_NOTIFICATIONS",
		"profile":              "STUDIOCTL_IMAGE_LOCALTEST_V2_PROFILE",
		"postgres":             "STUDIOCTL_IMAGE_LOCALTEST_V2_POSTGRES",
		"dialogporten-webapi":  "STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_WEBAPI",
		"dialogporten-graphql": "STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_GRAPHQL",
		"dialogporten-service": "STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_SERVICE",
		"dialogporten-adapter": "STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_ADAPTER",
		"correspondence":       "STUDIOCTL_IMAGE_LOCALTEST_V2_CORRESPONDENCE",
		"redis":                "STUDIOCTL_IMAGE_LOCALTEST_V2_REDIS",
		"receipt":              "STUDIOCTL_IMAGE_LOCALTEST_V2_RECEIPT",
		"arbeidsflate":         "STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE",
		"arbeidsflate-bff":     "STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE_BFF",
	}
	images := config.DefaultImages().LocaltestV2
	if got := images.ResearchNames(); !slices.Equal(slices.Sorted(slices.Values(got)), slices.Sorted(slices.Values(research))) {
		t.Errorf("research images = %v, want %v", got, research)
	}
	tag := manifest.MustDefault().Images.Tag
	if !regexp.MustCompile(`^sha-[0-9a-f]{7,40}$`).MatchString(tag) {
		t.Errorf("pinned tag %q, want sha-<commit>", tag)
	}
	for _, name := range research {
		spec := localtestV2Image(t, images, name)
		if want := "ghcr.io/ivarne-agent/localtest-research/localtest-" + name; spec.Image != want || spec.Tag != tag {
			t.Errorf("%s = %s:%s, want %s:%s", name, spec.Image, spec.Tag, want, tag)
		}
	}
	redis := localtestV2Image(t, images, "redis")
	if redis.Ref() != "docker.io/library/redis:7.2-alpine@sha256:29e8589c3f9ba699b5f7aa4b3c7733c58852a3626439e619aa0ee78de08c6ca0" ||
		redis.Floating {
		t.Errorf("redis = %+v", redis)
	}
	if !slices.Equal(images.Names(), slices.Sorted(maps.Keys(envs))) {
		t.Errorf("images = %v, want %v", images.Names(), slices.Sorted(maps.Keys(envs)))
	}
	for name, env := range envs {
		if got := config.LocaltestV2OverrideEnv(name); got != env {
			t.Errorf("override variable of %s = %s, want %s", name, got, env)
		}
	}
	if config.EnvLocaltestV2Tag() != "STUDIOCTL_LOCALTEST_V2_TAG" {
		t.Errorf("tag variable = %s", config.EnvLocaltestV2Tag())
	}
}

func TestRedisStaysPinnedWhenTheResearchTagMoves(t *testing.T) {
	t.Parallel()
	images := config.DefaultImages().LocaltestV2
	if slices.Contains(images.ResearchNames(), "redis") {
		t.Error("Redis is a third-party image: a research build tag does not apply to it")
	}
	images.UseTag("latest")
	if redis := localtestV2Image(t, images, "redis"); redis.Floating || !strings.Contains(redis.Tag, "@sha256:") {
		t.Errorf("Redis must stay pinned by digest, got %s:%s floating %v", redis.Image, redis.Tag, redis.Floating)
	}
}

func TestLocaltestV2ImagesCanBeOverriddenOneByOne(t *testing.T) {
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE", "localhost/localtest-v2-arbeidsflate:dev")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE_BFF", "localhost/localtest-v2-arbeidsflate-bff:dev")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_REDIS", "localhost:5000/redis:7")
	images := config.DefaultImages().LocaltestV2
	for _, name := range []string{"arbeidsflate", "arbeidsflate-bff"} {
		spec := localtestV2Image(t, images, name)
		if spec.Image != "localhost/localtest-v2-"+name || spec.Tag != "dev" || spec.Floating {
			t.Errorf("override not applied: %s:%s floating %v", spec.Image, spec.Tag, spec.Floating)
		}
	}
	if redis := localtestV2Image(t, images, "redis"); redis.Ref() != "localhost:5000/redis:7" {
		t.Errorf("redis override = %s", redis.Ref())
	}
	if storage := localtestV2Image(t, images, "storage"); storage.Image != "ghcr.io/ivarne-agent/localtest-research/localtest-storage" {
		t.Errorf("storage = %s, want the research build", storage.Image)
	}
}

func TestTheTagVariableRunsEveryResearchImageAtOneTag(t *testing.T) {
	t.Setenv("STUDIOCTL_LOCALTEST_V2_TAG", "latest")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE", "localhost/storage:mine")
	images := config.DefaultImages().LocaltestV2
	if spec := localtestV2Image(t, images, "register"); spec.Tag != "latest" || !spec.Floating {
		t.Errorf("register = %+v, want latest and floating", spec)
	}
	if spec := localtestV2Image(t, images, "storage"); spec.Ref() != "localhost/storage:mine" || spec.Floating {
		t.Errorf("storage = %+v, want the override, which wins over the tag", spec)
	}
}

func TestTheManifestMirrorsTheCoreImages(t *testing.T) {
	t.Parallel()
	core := config.DefaultImages().Core
	mirrored := manifest.MustDefault().Images.Core
	for name, spec := range map[string]config.ImageSpec{"pdf3": core.PDF3, "workflow-engine": core.WorkflowEngine} {
		m := mirrored[name]
		if m.Repository != spec.Image || m.Tag != spec.Tag || m.Floating != spec.Floating {
			t.Errorf("manifest images.core.%s = %s:%s floating %v, want %s:%s floating %v",
				name, m.Repository, m.Tag, m.Floating, spec.Image, spec.Tag, spec.Floating)
		}
	}
	if mirrored["pdf3"].OverrideEnv != "STUDIOCTL_IMAGE_PDF3" ||
		mirrored["workflow-engine"].OverrideEnv != "STUDIOCTL_IMAGE_WORKFLOW_ENGINE" {
		t.Errorf("manifest core override variables = %+v", mirrored)
	}
}
