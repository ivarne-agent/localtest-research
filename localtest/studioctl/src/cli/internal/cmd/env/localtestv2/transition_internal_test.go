package localtestv2

import (
	"fmt"
	"reflect"
	"slices"
	"testing"

	"altinn.studio/devenv/pkg/resource"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
)

// TestTheManifestLoaderReproducesTheHandWrittenTable builds the environment from the manifest and from the hand-written
// table it replaced, for every option combination, and requires the same resource graph, route table, health targets,
// container names, dev builds, groups and volumes.
func TestTheManifestLoaderReproducesTheHandWrittenTable(t *testing.T) {
	t.Setenv("STUDIOCTL_LOCALTEST_V2_TAG", "")
	defaults := config.DefaultImages()
	latest := config.DefaultImages()
	latest.LocaltestV2 = latest.LocaltestV2.WithTag("latest")
	t.Setenv("STUDIOCTL_LOCALTEST_V2_TAG", "sha-0123456")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE", "localhost:5000/storage:mine")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_REDIS", "localhost:5000/redis")
	t.Setenv("STUDIOCTL_IMAGE_LOCALTEST_V2_ARBEIDSFLATE_BFF", "localhost/bff:1")
	t.Setenv("STUDIOCTL_IMAGE_PDF3", "localhost/pdf3:2")
	t.Setenv("STUDIOCTL_IMAGE_WORKFLOW_ENGINE", "localhost/workflow-engine:3")
	overridden := config.DefaultImages()

	if got, want := DevBuildable(), legacyDevBuildable(); !slices.Equal(got, want) {
		t.Fatalf("DevBuildable = %v, want %v", got, want)
	}
	if got, want := groupTable(), legacyGroupTable(); !reflect.DeepEqual(got, want) {
		t.Fatalf("groupTable = %+v, want %+v", got, want)
	}
	if got, want := stack().VolumeNames(), legacyVolumes(); !slices.Equal(got, want) {
		t.Fatalf("volumes = %v, want %v", got, want)
	}
	if stack().Network != networkName || stack().ContainerPrefix != containerPrefix {
		t.Fatalf("network %q, prefix %q", stack().Network, stack().ContainerPrefix)
	}

	groupSets := make([]map[string]bool, 0, 2+len(GroupNames()))
	groupSets = append(groupSets, nil, allGroups())
	for _, name := range GroupNames() {
		resolved, err := ResolveGroups([]string{name}, nil)
		if err != nil {
			t.Fatal(err)
		}
		groupSets = append(groupSets, groupSet(resolved))
	}
	devSets := make([]map[string]bool, 0, 2+len(DevBuildable()))
	devSets = append(devSets, nil, groupSet(DevBuildable()))
	for _, name := range DevBuildable() {
		devSets = append(devSets, groupSet([]string{name}))
	}
	type user struct {
		runtimeUser, usernsMode string
		relabel                 bool
	}
	cases := 0
	for imageName, images := range map[string]config.ImagesConfig{
		"default": defaults, "latest": latest, "overridden": overridden,
	} {
		for _, port := range []string{"8000", "80", "18000"} {
			for _, u := range []user{{"", "", false}, {"1000:1000", "keep-id", true}} {
				for _, groups := range groupSets {
					for _, dev := range devSets {
						opts := &Options{
							DevComponents: dev,
							Images:        images,
							Topology:      envtopology.NewLocal(port),
							Groups:        groups,
							CertDir:       "/home/dev/.altinn-studio/certs",
							DevSourceRoot: "/src/localtest",
							RuntimeUser:   u.runtimeUser,
							UsernsMode:    u.usernsMode,
							RelabelBinds:  u.relabel,
						}
						name := fmt.Sprintf("images=%s port=%s user=%v groups=%v dev=%v",
							imageName, port, u, groupNames(groups), groupNames(dev))
						compareEnvironments(t, name, opts)
						cases++
					}
				}
			}
		}
	}
	t.Logf("%d option combinations", cases)
}

func compareEnvironments(t *testing.T, name string, opts *Options) {
	t.Helper()
	got, want := describeResources(resources(opts)), describeResources(legacyResources(opts))
	if len(got) != len(want) {
		t.Fatalf("%s: %d resources, want %d", name, len(got), len(want))
	}
	for i := range got {
		if !reflect.DeepEqual(got[i], want[i]) {
			t.Fatalf("%s: resource %d\n got %+v\nwant %+v", name, i, got[i], want[i])
		}
	}
	if got, want := boundTopologyConfig(opts), legacyBoundTopologyConfig(opts); !reflect.DeepEqual(got, want) {
		t.Fatalf("%s: route table\n got %+v\nwant %+v", name, got, want)
	}
	if got, want := healthTargets(opts), legacyHealthTargets(opts); !reflect.DeepEqual(got, want) {
		t.Fatalf("%s: health targets\n got %+v\nwant %+v", name, got, want)
	}
	if got, want := containerNames(opts), legacyContainerNames(opts); !slices.Equal(got, want) {
		t.Fatalf("%s: containers %v, want %v", name, got, want)
	}
}

// describedContainer is a container with its references replaced by the referenced IDs, so a mismatch prints readably.
type describedContainer struct {
	resource.Container

	Image     resource.ResourceID
	Networks  []resource.ResourceID
	DependsOn []resource.ResourceID
}

func describeResources(resources []resource.Resource) []any {
	out := make([]any, 0, len(resources))
	for _, r := range resources {
		switch v := r.(type) {
		case *resource.Container:
			described := describedContainer{
				Container: *v,
				Image:     v.Image.ID(),
				Networks:  refIDs(v.Networks),
				DependsOn: refIDs(v.DependsOn),
			}
			described.Container.Image = resource.RefID("")
			described.Container.Networks = nil
			described.Container.DependsOn = nil
			out = append(out, described)
		case *resource.Network:
			out = append(out, *v)
		case *resource.PulledImage:
			out = append(out, *v)
		case *resource.BuiltImage:
			out = append(out, *v)
		default:
			out = append(out, r)
		}
	}
	return out
}

func refIDs(refs []resource.ResourceRef) []resource.ResourceID {
	ids := make([]resource.ResourceID, 0, len(refs))
	for _, ref := range refs {
		ids = append(ids, ref.ID())
	}
	return ids
}
