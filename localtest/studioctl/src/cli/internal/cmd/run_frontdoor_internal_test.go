package cmd

import (
	"encoding/json"
	"testing"
	"time"
)

func TestARunWaitsForAFreshSuccessfulRegistrationAtItsAddress(t *testing.T) {
	t.Parallel()
	since := time.Date(2026, 10, 22, 12, 0, 0, 0, time.UTC)
	routes := func(body string) []frontDoorRoute {
		var r []frontDoorRoute
		if err := json.Unmarshal([]byte(body), &r); err != nil {
			t.Fatal(err)
		}
		return r
	}
	cases := map[string]struct {
		body string
		want bool
	}{
		"not registered":  {`[{"org":"ttd","app":"other","url":"http://host.docker.internal:5005"}]`, false},
		"another address": {`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5006"}]`, false},
		"provisioning":    {`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5005"}]`, false},
		"stale": {
			`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5005",` +
				`"provisioning":{"state":"succeeded","at":"2026-10-22T11:59:00Z"}}]`,
			false,
		},
		"failed": {
			`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5005",` +
				`"provisioning":{"state":"failed","at":"2026-10-22T12:00:01Z","error":"no"}}]`,
			false,
		},
		"provisioned before, in another zone": {
			`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5005",` +
				`"provisioning":{"state":"succeeded","at":"2026-10-22T12:00:01.5+02:00"}},` +
				`{"org":"ttd","app":"app2","url":"x"}]`,
			false,
		},
		"provisioned now": {
			`[{"org":"ttd","app":"app","url":"http://host.docker.internal:5005",` +
				`"provisioning":{"state":"succeeded","at":"2026-10-22T12:00:01.5Z"}}]`,
			true,
		},
	}
	for name, c := range cases {
		got, state := registered(routes(c.body), "ttd", "app", frontDoorAppURL(5005), since)
		if got != c.want {
			t.Errorf("%s: registered = %v (%s), want %v", name, got, state, c.want)
		}
	}
}
