package cmd

import (
	"bytes"
	"io"
	"strings"
	"testing"

	envlocaltest "altinn.studio/studioctl/internal/cmd/env/localtest"
	"altinn.studio/studioctl/internal/ui"
)

func TestParseUpFlagsMinimal(t *testing.T) {
	t.Parallel()
	command := &EnvCommand{cfg: nil, out: nil}
	flags, _, err := command.parseUpFlags([]string{"-r", runtimeLocaltestV2, "--minimal"})
	if err != nil || !flags.minimal {
		t.Fatalf("parseUpFlags() = %+v, %v", flags, err)
	}
}

func TestTheUsageNamesMinimal(t *testing.T) {
	t.Parallel()
	var out bytes.Buffer
	command := &EnvCommand{cfg: nil, out: ui.NewOutput(&out, io.Discard, false)}
	if _, shown, err := command.parseUpFlags([]string{"--help"}); err != nil || !shown {
		t.Fatalf("parseUpFlags(--help) = %v, %v", shown, err)
	}
	if !strings.Contains(out.String(), "--minimal") {
		t.Fatalf("usage without --minimal:\n%s", out.String())
	}
}

func TestTheStatusOfMinimalModeNamesTheModeAndTheEmulatedServices(t *testing.T) {
	t.Parallel()
	status := &envlocaltest.Status{
		Mode: "minimal",
		Emulated: []string{
			"register", "profile", "authentication", "authorization", "access-management",
			"events", "resource-registry", "notifications", "pdf3",
		},
		Containers: []envlocaltest.ContainerStatus{
			{Name: "localtest-v2-postgres", Image: "", ImageID: "", Status: "running"},
		},
		Running:    true,
		AnyRunning: true,
	}
	var out bytes.Buffer
	if err := (envStatusOutput{Status: status, Runtime: runtimeLocaltestV2, JSONOutput: false}).Print(
		ui.NewOutput(&out, io.Discard, false),
	); err != nil {
		t.Fatal(err)
	}
	text := out.String()
	if !strings.HasPrefix(text, "localtest-v2 is running (minimal mode)\n") {
		t.Errorf("first line of %q", text)
	}
	const emulated = "register, profile, authentication, authorization, access-management,\n" +
		"events, resource-registry, notifications, pdf3: emulated in localtest\n"
	if !strings.HasSuffix(text, emulated) {
		t.Errorf("status %q does not end with %q", text, emulated)
	}

	status.Mode, status.Emulated = "", nil
	out.Reset()
	if err := (envStatusOutput{Status: status, Runtime: runtimeLocaltestV2, JSONOutput: false}).Print(
		ui.NewOutput(&out, io.Discard, false),
	); err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(out.String(), "localtest-v2 is running.\n") || strings.Contains(out.String(), "emulated") {
		t.Errorf("default stack status %q", out.String())
	}
}
