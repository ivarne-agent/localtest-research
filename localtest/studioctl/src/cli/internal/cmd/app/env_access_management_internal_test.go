package app

import (
	"slices"
	"strings"
	"testing"

	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

const accessManagementLinkPrefix = "PlatformFrontendSettings__AccessManagement"

func accessManagementLinks(topology envtopology.Local, signing *accessTokenSigning) []string {
	var links []string
	for _, entry := range newAppRunEnv(nil, "http://127.0.0.1:5005", topology, "", goldenSecretsDir, "", signing) {
		if strings.HasPrefix(entry, accessManagementLinkPrefix) {
			links = append(links, entry)
		}
	}
	return links
}

// With the access-management group the party switch and the access management link go to the GUI on am.ui.<host>,
// following the public origin; without it (minimal mode included) and without localtest-v2 both stay unset.
func TestTheAccessManagementLinksFollowTheAccessManagementGroup(t *testing.T) {
	t.Parallel()
	signing := accessTokenSigning{
		FullPath:         goldenCertDir + "/" + localtestv2.CertificateFileName,
		KeysFolder:       "/certs/",
		FileName:         localtestv2.CertificateFileName,
		arbeidsflate:     true,
		accessManagement: false,
	}
	if links := accessManagementLinks(envtopology.NewLocal("8000"), &signing); len(links) != 0 {
		t.Fatalf("without the group: %v, want none", links)
	}
	signing.accessManagement = true
	want := []string{
		"PlatformFrontendSettings__AccessManagementChangeAndRedirectUrl=" +
			"http://am.ui.local.altinn.cloud:8000/accessmanagement/api/v1/reportee/changeandredirect" +
			"?partyId={partyId}&goTo={goTo}",
		"PlatformFrontendSettings__AccessManagementUrl=http://am.ui.local.altinn.cloud:8000/accessmanagement/ui",
	}
	if links := accessManagementLinks(envtopology.NewLocal("8000"), &signing); !slices.Equal(links, want) {
		t.Fatalf("with the group: %v, want %v", links, want)
	}
	want = []string{
		"PlatformFrontendSettings__AccessManagementChangeAndRedirectUrl=" +
			"http://am.ui.origin.test:18000/accessmanagement/api/v1/reportee/changeandredirect" +
			"?partyId={partyId}&goTo={goTo}",
		"PlatformFrontendSettings__AccessManagementUrl=http://am.ui.origin.test:18000/accessmanagement/ui",
	}
	if links := accessManagementLinks(envtopology.NewLocalAt("origin.test", "18000"), &signing); !slices.Equal(
		links,
		want,
	) {
		t.Fatalf("at another origin: %v, want %v", links, want)
	}
	if links := accessManagementLinks(envtopology.NewLocal("8000"), nil); len(links) != 0 {
		t.Fatalf("without localtest-v2: %v, want none", links)
	}
}
