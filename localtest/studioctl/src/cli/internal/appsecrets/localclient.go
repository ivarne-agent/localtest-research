package appsecrets

import (
	"bytes"
	"crypto/rand"
	"crypto/rsa"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"math/big"
	"net/url"
	"os"
	"strings"

	"altinn.studio/studioctl/internal/osutil"
)

// The local Maskinporten client: what a run against localtest-v2 gets when the developer has stored no client of
// their own. It points at the stack's Maskinporten fake, so the app libraries' real MaskinportenClient runs on
// localtest-v2 as it runs in a cluster: a signed grant to the fake, then production Authentication's
// exchange/maskinporten, which names the app's org. The file is the one a developer's client goes in, in the same
// format, so the app reads it the way it reads the operator's file.
const (
	// LocalAuthorityPath is the fake's path on the public origin. The trailing slash matters: the app libraries
	// resolve the discovery document relative to the authority.
	LocalAuthorityPath = "/localtest/api/maskinporten/"

	// localClientIDSuffix follows the org in the local client's id, <org>-localtest.
	localClientIDSuffix = "-localtest"

	localKeyBits     = 2048
	localKeyIDLength = 8
)

// LocalAuthority is the authority of the local client for a stack at origin (scheme, host and port).
func LocalAuthority(origin string) string {
	return strings.TrimRight(origin, "/") + LocalAuthorityPath
}

// LocalClientID is the local client's id for an org.
func LocalClientID(org string) string {
	return org + localClientIDSuffix
}

// IsLocal reports whether the client is one studioctl (or the Aspire package) generated for localtest-v2, rather
// than one the developer stored: its id ends in -localtest and its authority is the stack's Maskinporten fake. A
// developer cannot store such a client through set, which accepts an https authority only, and the fake's path
// is the stack's own.
func (c MaskinportenClient) IsLocal() bool {
	if !strings.HasSuffix(c.ClientID, localClientIDSuffix) {
		return false
	}
	parsed, err := url.Parse(c.Authority)
	return err == nil && parsed.Host != "" && parsed.Path == LocalAuthorityPath
}

// ProvisionLocalMaskinportenClient writes the local client for a run against localtest-v2 at origin, unless the
// developer stored a client of their own, which is never overwritten and which the stack's exchange trusts too (its
// test or production Maskinporten is in the manifest's list); an unreadable file is left alone too: it is the
// developer's to fix, and doctor reports it. It reports whether the file was written (created, a new key, or
// another origin). The key is generated once per app and kept: a later run reuses
// the key of the local client already there and rewrites the file only when the origin moved. Nothing rotates it,
// and nothing in the file expires (the JWK carries no validity): rotation is the operator's in a cluster, out of
// scope locally. The write is atomic and owner-only, as for a developer's client.
func ProvisionLocalMaskinportenClient(dir, origin, org string) (bool, error) {
	existing, local, err := readStoredClient(dir)
	switch {
	case errors.Is(err, ErrNoMaskinportenClient):
	case err != nil:
		// An unreadable file is the developer's to fix (doctor reports it), never replaced here.
		return false, nil //nolint:nilerr // Not a failure of the run: the file is left as it is.
	case !local:
		return false, nil
	}

	jwk := existing.Jwk
	if !local || len(jwk) == 0 {
		if jwk, err = GenerateLocalKey(); err != nil {
			return false, err
		}
	}
	payload, err := EncodeLocalMaskinportenClient(origin, org, jwk)
	if err != nil {
		return false, err
	}
	path := MaskinportenPath(dir)
	current, readErr := os.ReadFile(path) //nolint:gosec // The path is under the configured studioctl home.
	if readErr == nil && bytes.Equal(current, payload) {
		return false, nil
	}
	if err = EnsureDir(dir); err != nil {
		return false, err
	}
	err = osutil.WriteFileAtomic(
		path,
		payload,
		osutil.AtomicWriteOptions{Perm: osutil.FilePermOwnerOnly, OwnerOnly: true},
	)
	if err != nil {
		return false, fmt.Errorf("store the local %s: %w", MaskinportenFileName, err)
	}
	return true, nil
}

// EncodeLocalMaskinportenClient is the local client's file: the operator's format, the client wrapped in
// MaskinportenSettings, as StoreMaskinportenClient writes it. The Aspire package writes the same bytes for the same
// origin, org and key (the shared golden file testdata/local-maskinporten.golden.json).
func EncodeLocalMaskinportenClient(origin, org string, jwk json.RawMessage) ([]byte, error) {
	client := MaskinportenClient{
		Authority: LocalAuthority(origin),
		ClientID:  LocalClientID(org),
		JwkBase64: "",
		Jwk:       jwk,
	}
	if _, err := client.privateKey(); err != nil {
		return nil, err
	}
	return encodeClientFile(client)
}

// GenerateLocalKey generates the local client's key: a 2048-bit RSA key as a JWK with every field the app libraries'
// converter requires.
func GenerateLocalKey() (json.RawMessage, error) {
	key, err := rsa.GenerateKey(rand.Reader, localKeyBits)
	if err != nil {
		return nil, fmt.Errorf("generate the local Maskinporten key: %w", err)
	}
	key.Precompute()
	id := make([]byte, localKeyIDLength)
	if _, err = rand.Read(id); err != nil {
		return nil, fmt.Errorf("generate the local Maskinporten key id: %w", err)
	}
	encode := func(value *big.Int) string { return base64.RawURLEncoding.EncodeToString(value.Bytes()) }
	jwk := privateKey{
		Kty: "RSA",
		Use: "sig",
		Kid: "localtest-" + hex.EncodeToString(id),
		Alg: "RS256",
		N:   encode(key.N),
		E:   encode(big.NewInt(int64(key.E))),
		D:   encode(key.D),
		P:   encode(key.Primes[0]),
		Q:   encode(key.Primes[1]),
		Qi:  encode(key.Precomputed.Qinv),
		Dp:  encode(key.Precomputed.Dp),
		Dq:  encode(key.Precomputed.Dq),
	}
	raw, err := json.Marshal(jwk)
	if err != nil {
		return nil, fmt.Errorf("encode the local Maskinporten key: %w", err)
	}
	return raw, nil
}

// readStoredClient reads the file in the secrets directory and says whether it is the local client. The local client
// is recognised before validation, which refuses its http authority as a developer's client.
func readStoredClient(dir string) (MaskinportenClient, bool, error) {
	data, err := os.ReadFile(MaskinportenPath(dir))
	if errors.Is(err, os.ErrNotExist) {
		return MaskinportenClient{}, false, ErrNoMaskinportenClient
	}
	if err != nil {
		return MaskinportenClient{}, false, fmt.Errorf("read the stored Maskinporten client: %w", err)
	}
	var file map[string]MaskinportenClient
	if json.Unmarshal(data, &file) == nil {
		if client, ok := file[wrapperKey]; ok && client.IsLocal() {
			return client, true, nil
		}
	}
	client, err := ParseMaskinportenClient(data)
	if err != nil {
		return MaskinportenClient{}, false, fmt.Errorf(
			"the stored Maskinporten client is unreadable (%w); remove it and store it again",
			err,
		)
	}
	return client, false, nil
}

func encodeClientFile(client MaskinportenClient) ([]byte, error) {
	payload, err := json.MarshalIndent(map[string]MaskinportenClient{wrapperKey: client}, "", "  ")
	if err != nil {
		return nil, fmt.Errorf("encode Maskinporten client: %w", err)
	}
	return append(payload, '\n'), nil
}
