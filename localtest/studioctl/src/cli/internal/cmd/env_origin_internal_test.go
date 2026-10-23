package cmd

import (
	"bytes"
	"errors"
	"io"
	"strings"
	"testing"

	envlocaltest "altinn.studio/studioctl/internal/cmd/env/localtest"
	"altinn.studio/studioctl/internal/ui"
)

func TestParseUpFlagsHostAndPort(t *testing.T) {
	t.Parallel()
	command := &EnvCommand{cfg: nil, out: nil}
	flags, _, err := command.parseUpFlags([]string{"-r", runtimeLocaltestV2, "--host", "origin.test", "--port", "0"})
	if err != nil || flags.publicHost != "origin.test" || flags.publicPort == nil || *flags.publicPort != 0 {
		t.Fatalf("parseUpFlags() = %+v, %v", flags, err)
	}
	flags, _, err = command.parseUpFlags([]string{"-r", runtimeLocaltestV2})
	if err != nil || flags.publicHost != "" || flags.publicPort != nil {
		t.Fatalf("no origin flags: %+v, %v", flags, err)
	}
	if _, _, err := command.parseUpFlags([]string{"--port", "eighty"}); err == nil {
		t.Fatal("--port eighty parsed")
	}
}

func TestTheLocaltestRuntimeRefusesTheOriginFlags(t *testing.T) {
	t.Parallel()
	port := 18000
	err := validateUpFlagsForRuntime(envUpFlags{
		runtime: runtimeLocaltest, publicHost: "origin.test", publicPort: &port,
	})
	if !errors.Is(err, ErrInvalidFlagValue) || !strings.Contains(err.Error(), "--host, --port") {
		t.Fatalf("validateUpFlagsForRuntime() = %v", err)
	}
}

func TestTheStatusNamesTheOrigin(t *testing.T) {
	t.Parallel()
	status := &envlocaltest.Status{
		Origin: "http://origin.test:18000",
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
	if !strings.HasPrefix(out.String(), "localtest-v2 is running.\nOrigin: http://origin.test:18000\n") {
		t.Errorf("status %q", out.String())
	}
}
