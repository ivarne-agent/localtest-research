package appsecrets_test

import (
	"bytes"
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"math/big"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"altinn.studio/studioctl/internal/appsecrets"
)

const (
	defaultOrigin = "http://local.altinn.cloud:8000"
	customOrigin  = "http://local.altinn.test:18000"
)

// localClientGoldenPath holds the bytes of the local client for the default origin, org ttd and a placeholder key. The
// Aspire package writes the same bytes for the same input and compares them with this file.
//
//nolint:gochecknoglobals // A test fixture path.
var localClientGoldenPath = filepath.Join("testdata", "local-maskinporten.golden.json")

func TestTheLocalClientMatchesTheGoldenFile(t *testing.T) {
	t.Parallel()
	got, err := appsecrets.EncodeLocalMaskinportenClient(defaultOrigin, "ttd", json.RawMessage(testJwk))
	if err != nil {
		t.Fatal(err)
	}
	if os.Getenv("STUDIOCTL_UPDATE_GOLDEN") != "" {
		if writeErr := os.WriteFile(localClientGoldenPath, got, 0o600); writeErr != nil {
			t.Fatal(writeErr)
		}
		return
	}
	want, err := os.ReadFile(localClientGoldenPath)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(got, want) {
		t.Errorf("the local client differs from %s:\n%s", localClientGoldenPath, got)
	}
}

func readLocalClient(t *testing.T, dir string) appsecrets.MaskinportenClient {
	t.Helper()
	data, err := os.ReadFile(appsecrets.MaskinportenPath(dir))
	if err != nil {
		t.Fatal(err)
	}
	var file struct {
		Settings appsecrets.MaskinportenClient `json:"MaskinportenSettings"` //nolint:tagliatelle // The file's format.
	}
	if err := json.Unmarshal(data, &file); err != nil {
		t.Fatal(err)
	}
	return file.Settings
}

func TestProvisionLocalMaskinportenClient_WritesTheFakesClient(t *testing.T) {
	t.Parallel()
	dir := filepath.Join(t.TempDir(), "secrets")
	result, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd")
	if err != nil {
		t.Fatal(err)
	}
	if !result.Written || result.Developer != nil {
		t.Fatalf("result = %+v, want written, no developer client", result)
	}
	client := readLocalClient(t, dir)
	if client.Authority != "http://local.altinn.cloud:8000/localtest/api/maskinporten/" ||
		client.ClientID != "ttd-localtest" || !client.IsLocal() {
		t.Fatalf("client = %s at %s, want ttd-localtest at the fake", client.ClientID, client.Authority)
	}
	if runtime.GOOS != "windows" {
		info, statErr := os.Stat(appsecrets.MaskinportenPath(dir))
		if statErr != nil || info.Mode().Perm() != 0o600 {
			t.Fatalf("mode = %v (%v), want 0600", info.Mode().Perm(), statErr)
		}
	}
	assertSigningKey(t, client.Jwk)
}

// The generated key is a working RSA key: the app libraries sign the grant with it.
func assertSigningKey(t *testing.T, raw json.RawMessage) {
	t.Helper()
	var jwk map[string]string
	if err := json.Unmarshal(raw, &jwk); err != nil {
		t.Fatal(err)
	}
	for _, field := range []string{"kty", "use", "kid", "alg", "n", "e", "d", "p", "q", "qi", "dp", "dq"} {
		if jwk[field] == "" {
			t.Fatalf("jwk has no %s", field)
		}
	}
	if jwk["kty"] != "RSA" || jwk["alg"] != "RS256" || jwk["use"] != "sig" {
		t.Fatalf("jwk kty/alg/use = %s/%s/%s", jwk["kty"], jwk["alg"], jwk["use"])
	}
	number := func(field string) *big.Int {
		value, err := base64.RawURLEncoding.DecodeString(jwk[field])
		if err != nil {
			t.Fatalf("%s is not base64url: %v", field, err)
		}
		return new(big.Int).SetBytes(value)
	}
	key := &rsa.PrivateKey{
		PublicKey: rsa.PublicKey{N: number("n"), E: int(number("e").Int64())},
		D:         number("d"),
		Primes:    []*big.Int{number("p"), number("q")},
	}
	if err := key.Validate(); err != nil {
		t.Fatalf("the key is not a valid RSA key: %v", err)
	}
	if key.N.BitLen() < 2048 {
		t.Fatalf("modulus %d bits, want 2048", key.N.BitLen())
	}
	digest := sha256.Sum256([]byte("grant"))
	signature, err := rsa.SignPKCS1v15(rand.Reader, key, crypto.SHA256, digest[:])
	if err != nil {
		t.Fatal(err)
	}
	if err := rsa.VerifyPKCS1v15(&key.PublicKey, crypto.SHA256, digest[:], signature); err != nil {
		t.Fatal(err)
	}
}

func TestProvisionLocalMaskinportenClient_KeepsTheKeyAndFollowsTheOrigin(t *testing.T) {
	t.Parallel()
	dir := t.TempDir()
	if _, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd"); err != nil {
		t.Fatal(err)
	}
	first := readLocalClient(t, dir)

	again, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd")
	if err != nil || again.Written {
		t.Fatalf("second run: %+v, %v; want nothing written", again, err)
	}

	moved, err := appsecrets.ProvisionLocalMaskinportenClient(dir, customOrigin, "ttd")
	if err != nil || !moved.Written {
		t.Fatalf("other origin: %+v, %v; want written", moved, err)
	}
	second := readLocalClient(t, dir)
	if second.Authority != customOrigin+"/localtest/api/maskinporten/" {
		t.Fatalf("authority = %s, want the custom origin's fake", second.Authority)
	}
	if compact(t, first.Jwk) != compact(t, second.Jwk) {
		t.Fatal("the key changed with the origin, want it kept")
	}
}

func compact(t *testing.T, raw json.RawMessage) string {
	t.Helper()
	var buffer bytes.Buffer
	if err := json.Compact(&buffer, raw); err != nil {
		t.Fatal(err)
	}
	return buffer.String()
}

func readFile(t *testing.T, path string) string {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	return string(data)
}

func TestProvisionLocalMaskinportenClient_LeavesTheDevelopersClient(t *testing.T) {
	t.Parallel()
	dir := t.TempDir()
	if _, err := appsecrets.StoreMaskinportenClient(dir, appsecrets.MaskinportenClient{
		Authority: testAuthority, ClientID: "real-client", Jwk: json.RawMessage(testJwk), JwkBase64: "",
	}); err != nil {
		t.Fatal(err)
	}
	before := readFile(t, appsecrets.MaskinportenPath(dir))
	result, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd")
	if err != nil {
		t.Fatal(err)
	}
	if result.Written || result.Developer == nil || result.Developer.ClientID != "real-client" {
		t.Fatalf("result = %+v, want the developer's client reported and nothing written", result)
	}
	if after := readFile(t, appsecrets.MaskinportenPath(dir)); before != after {
		t.Fatal("the developer's client was changed")
	}
}

func TestProvisionLocalMaskinportenClient_LeavesAnUnreadableFile(t *testing.T) {
	t.Parallel()
	dir := t.TempDir()
	if err := os.WriteFile(appsecrets.MaskinportenPath(dir), []byte("{not json"), 0o600); err != nil {
		t.Fatal(err)
	}
	result, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd")
	if err != nil || result.Written {
		t.Fatalf("result = %+v, %v; want the file left alone", result, err)
	}
	if data := readFile(t, appsecrets.MaskinportenPath(dir)); data != "{not json" {
		t.Fatalf("file = %s, want it unchanged", data)
	}
}

// The local client is not the developer's: show and doctor see none, remove leaves it, set replaces it.
func TestTheLocalClientIsNotTheDevelopers(t *testing.T) {
	t.Parallel()
	dir := t.TempDir()
	if _, err := appsecrets.ProvisionLocalMaskinportenClient(dir, defaultOrigin, "ttd"); err != nil {
		t.Fatal(err)
	}
	if _, loadErr := appsecrets.LoadMaskinportenClient(dir); !errors.Is(loadErr, appsecrets.ErrNoMaskinportenClient) {
		t.Fatalf("LoadMaskinportenClient() error = %v, want ErrNoMaskinportenClient", loadErr)
	}
	removed, err := appsecrets.RemoveMaskinportenClient(dir)
	if err != nil || removed {
		t.Fatalf("RemoveMaskinportenClient() = %v, %v; want nothing removed", removed, err)
	}
	if _, err = os.Stat(appsecrets.MaskinportenPath(dir)); err != nil {
		t.Fatalf("the local client is gone: %v", err)
	}
	if _, err = appsecrets.StoreMaskinportenClient(dir, appsecrets.MaskinportenClient{
		Authority: testAuthority, ClientID: "real-client", Jwk: json.RawMessage(testJwk), JwkBase64: "",
	}); err != nil {
		t.Fatal(err)
	}
	client, err := appsecrets.LoadMaskinportenClient(dir)
	if err != nil || client.ClientID != "real-client" {
		t.Fatalf("LoadMaskinportenClient() = %s, %v; want the stored client", client.ClientID, err)
	}
}

func TestIsLocal(t *testing.T) {
	t.Parallel()
	for _, tc := range []struct {
		authority, clientID string
		want                bool
	}{
		{defaultOrigin + "/localtest/api/maskinporten/", "ttd-localtest", true},
		{customOrigin + "/localtest/api/maskinporten/", "skd-localtest", true},
		{testAuthority, "ttd-localtest", false},
		{defaultOrigin + "/localtest/api/maskinporten/", "real-client", false},
		{defaultOrigin + "/localtest/api/maskinporten", "ttd-localtest", false},
	} {
		client := appsecrets.MaskinportenClient{Authority: tc.authority, ClientID: tc.clientID, Jwk: nil, JwkBase64: ""}
		if got := client.IsLocal(); got != tc.want {
			t.Errorf("IsLocal(%s, %s) = %v, want %v", tc.clientID, tc.authority, got, tc.want)
		}
	}
	if !strings.HasSuffix(appsecrets.LocalAuthority(defaultOrigin+"/"), "8000/localtest/api/maskinporten/") {
		t.Error("LocalAuthority doubles the slash")
	}
}
