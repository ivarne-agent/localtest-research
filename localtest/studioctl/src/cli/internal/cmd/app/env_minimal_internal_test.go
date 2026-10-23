package app

import (
	"strings"
	"testing"

	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

const arbeidsflateLinkPrefix = "PlatformFrontendSettings__Arbeidsflate"

func arbeidsflateLinks(signing *accessTokenSigning) []string {
	var links []string
	entries := newAppRunEnv(
		nil,
		"http://127.0.0.1:5005",
		envtopology.NewLocal("8000"),
		"",
		goldenSecretsDir,
		"",
		signing,
	)
	for _, entry := range entries {
		if strings.HasPrefix(entry, arbeidsflateLinkPrefix) {
			links = append(links, entry)
		}
	}
	return links
}

// Minimal mode has no Arbeidsflate, so a run against it leaves the links unset and the app frontend falls back to the
// localtest start page.
func TestARunAgainstMinimalModeLeavesTheArbeidsflateLinksUnset(t *testing.T) {
	t.Parallel()
	signing := accessTokenSigning{
		FullPath:   goldenCertDir + "/" + localtestv2.CertificateFileName,
		KeysFolder: "/certs/",
		FileName:   localtestv2.CertificateFileName,
		minimal:    false,
	}
	if links := arbeidsflateLinks(&signing); len(links) != 4 {
		t.Fatalf("the default stack: %v, want the four links", links)
	}
	signing.minimal = true
	if links := arbeidsflateLinks(&signing); len(links) != 0 {
		t.Fatalf("minimal mode: %v, want none", links)
	}
}

func TestTheSigningCarriesTheMinimalModeFromTheState(t *testing.T) {
	t.Parallel()
	for _, minimal := range []bool{false, true} {
		cfg := &config.Config{Home: t.TempDir(), DataDir: t.TempDir()}
		if err := localtestv2.WriteState(cfg, localtestv2.State{
			CertificateDir: localtestv2.CertificateDir(cfg),
			Groups:         nil,
			Dev:            nil,
			LatestImages:   false,
			Minimal:        minimal,
		}); err != nil {
			t.Fatal(err)
		}
		signing, err := NewService(cfg).nativeAccessTokenSigning(t.TempDir())
		if err != nil {
			t.Fatal(err)
		}
		if signing == nil || signing.minimal != minimal {
			t.Fatalf("minimal state %v: signing %+v", minimal, signing)
		}
	}
}
