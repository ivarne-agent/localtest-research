# HTTPS for the Aspire-hosted stack

**Superseded 2026-10-23 by task 27:** the front door this research terminates TLS on (the package's stock YARP container) is gone; the localtest container is the front door in both runtimes, so any HTTPS work starts there (task 24); see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

## Status (2026-10-22)

- **Research done** as task 23 of [`../../TODO.md`](../../TODO.md): can the Aspire-hosted stack serve
  `https://local.altinn.cloud:8000` with a self-signed or development certificate, and what does https change.
- Written against this repository at `d461e164` (main; package `localtest/aspire/Altinn.App.Aspire/` after 21e, images
  `sha-98e2bae`), `Altinn/altinn-studio` `origin/main` at `0a62afac02` (app-lib, pdf3), **Aspire 13.6.2**
  (`Aspire.Hosting`, `Aspire.Hosting.Yarp`, DCP linux-arm64), SDK 10.0.401, Podman 5.7 (rootful, behind its socket).
- **Run** (small experiments on this machine, one at a time, each stopped and cleaned up; no studioctl stack and no
  stack on port 8000): the dev-certs state; a CA and a server certificate made with `CertificateRequest`; the stock
  YARP image terminating TLS with that certificate (`podman run`); the CA trust of the Storage, localtest-app and
  Arbeidsflate-BFF images with `SSL_CERT_DIR`, `SSL_CERT_FILE` and `NODE_EXTRA_CA_CERTS` (a probe run inside each image);
  a **scratch single-file AppHost** that gives the YARP front door the certificate and two component images the CA with
  Aspire's own APIs, under Podman; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` with `CreateBuilder` and `CreateSlimBuilder`;
  a dev-cert-shaped certificate with the stack's SANs imported and trusted by `dotnet dev-certs` in an isolated `HOME`.
- **Only read**: the components' code and READMEs for forwarded headers, issuers and scheme-built URLs; the pdf3
  worker's entrypoint (read out of the image); studioctl and studioctl-server; Aspire's decompiled sources. **Not run**:
  the full stack on https, a browser, macOS, Windows, Firefox, Docker (see [Not verified](#not-verified)).
- No statement in another plan is superseded. One correction to [`localtest/reviews/cookies.md`](../../localtest/reviews/cookies.md)
  (line 23): Register, Access Management and the Resource Registry also call `UseForwardedHeaders` (through
  `Altinn.Authorization.ServiceDefaults`' `AddDefaultAltinnMiddleware`, `ForwardedHeaders.All` with `KnownIPNetworks`
  limited to loopback plus `Altinn:ClusterInfo:TrustedProxies`, which nothing sets), so the review's conclusion holds:
  no component honours `X-Forwarded-Proto` from the container network today.

## Answer in short

**Yes, and Aspire 13.6 does most of it.** `Aspire.Hosting.Yarp` terminates TLS itself when the resource has a
certificate (`WithHttpsCertificate(X509Certificate2)`, `WithHostHttpsPort(8000)`), and Aspire gives any container a
custom CA (`AddCertificateAuthorityCollection(...).WithCertificate(ca)`, `WithCertificateAuthorityCollection(...)`),
copying the PEMs into the container and setting `SSL_CERT_DIR`, so no image changes and no shell are needed. The
ASP.NET Core development certificate is **not** usable as is: it names `localhost` and the container host names only.
The package would generate its own CA and a server certificate for the ingress hosts in its data directory; the
developer trusts the CA once. The front door terminates TLS, the components stay on http inside and must be told the
request was https (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`); `{{platform}}` becomes `https://local.altinn.cloud:8000/`
from a scheme setting in the topology. Five hard-coded `http://af.local.altinn.cloud:8000` origins in the fakes must
become configuration. Effort: **4.5 to 6 days** for the Aspire package, **2 to 3 more** for studioctl parity.

## 1. What Aspire 13.6.2 offers

Read from the decompiled `Aspire.Hosting` and `Aspire.Hosting.Yarp` 13.6.2 and their XML documentation, then run (section 7).

| API (13.6.2) | What it does |
| --- | --- |
| `WithHttpsCertificate<T>(X509Certificate2, password?)` | Associates a server key pair with any resource (`HttpsCertificateAnnotation`). For a container, DCP writes it into the container under `/usr/lib/ssl/aspire/private/<thumbprint>.{crt,key,pem,pfx}` (`ContainerCreator.BuildContainerConfiguration`); no bind mount, so the host file's permissions do not matter |
| `WithHttpsCertificateConfiguration<T>(ctx => ...)` | Tells a resource where those files are (`ctx.CertificatePath`, `KeyPath`, `PfxPath`, `Password`) through environment or arguments |
| `WithHttpsDeveloperCertificate<T>()`, `WithoutHttpsCertificate<T>()` | The same with the ASP.NET Core development certificate, or none |
| `AddYarp(name)` | Registers `WithHttpsCertificateConfiguration` setting `Kestrel__Certificates__Default__Path`/`KeyPath`/`Password`, and a `BeforeStartEvent` handler (`SubscribeHttpsEndpointsUpdate`, experimental `ASPIRECERTIFICATES001`) that, **when the resource has a certificate** (its own, or the dev certificate when Aspire uses that for https), adds an `https` endpoint (target port 5001, host port from `WithHostHttpsPort`) and sets `ASPNETCORE_URLS=https://*:5001;http://*:5000` and `ASPNETCORE_HTTPS_PORT`. The `http` endpoint (5000) always stays |
| `WithHostHttpsPort(port)` | The host port of that `https` endpoint |
| `AddCertificateAuthorityCollection(name).WithCertificate(s)/WithCertificatesFromFile(pem)` | A set of CAs (a resource excluded from publishing) |
| `WithCertificateAuthorityCollection<T>(collection)` | Trust those CAs in a resource at run time. Default scope `Append`: DCP writes the PEMs to `/usr/lib/ssl/aspire/certs` and a bundle to `/usr/lib/ssl/aspire/cert.pem`, and sets `SSL_CERT_DIR=/usr/lib/ssl/aspire/certs:/etc/ssl/certs:/usr/local/share/ca-certificates:/etc/pki/tls/certs`; `Override`/`System` also set `SSL_CERT_FILE` and overwrite the bundle paths |
| `WithCertificateTrustConfiguration<T>(ctx => ...)` | A callback with `ctx.CertificateBundlePath` (`/usr/lib/ssl/aspire/cert.pem`) and `ctx.CertificateDirectoriesPath`, for runtimes that ignore `SSL_CERT_DIR` (Node, Chromium) |
| `WithContainerCertificatePaths<T>(...)` | Other trust paths for an unusual image |
| `WithDeveloperCertificateTrust<T>(bool)`, `DistributedApplicationOptions.TrustDeveloperCertificate` | Whether the dev certificate is added to a resource's trust. Default **true** (`ASPIRE_DEVELOPER_CERTIFICATE_DEFAULT_TRUST`), but only a **trusted** dev certificate counts |
| Project resources (`ExecutableConfigurationResolver`) | The same trust for a process on the host: the CAs written to a directory under the AppHost and `SSL_CERT_DIR` set to it plus the existing value or the Linux defaults. **Only OpenSSL reads `SSL_CERT_DIR`**: .NET on Linux honours it, .NET on macOS and Windows uses the keychain or the certificate store |

When Aspire uses the dev certificate (`DeveloperCertificateService`): `UseForHttps` is true only if
`ASPIRE_DEVELOPER_CERTIFICATE_DEFAULT_HTTPS_TERMINATION` is not false, trust is on, and a **trusted** dev certificate
with a private key is in `CurrentUser/My`. Container trust of the dev certificate also needs a dev certificate of
version 4 or later (`SupportsContainerTrust`). On this machine the dev certificate is untrusted (`dotnet dev-certs https
--check --trust`: "none of them is trusted"), so the AppHost logs "No trusted Aspire development certificate was found"
and does neither, which is why 21d's front door is http only and the dashboard needs the `ASPIRE_ALLOW_UNSECURED_TRANSPORT`
variables.

**Side finding, read only.** On a developer machine where the dev certificate *is* trusted (the usual case on macOS
and Windows after `dotnet dev-certs https --trust`), today's package front door gets that extra `https` endpoint with
the `localhost` certificate on a DCP-proxied random port, and every component gets `SSL_CERT_DIR` with the dev
certificate appended. Both are harmless for http on port 8000 but make the containers' environment differ between
machines, which recreates persistent containers when the trust state changes. An http-mode front door should call
`WithoutHttpsCertificate()`, and the components could set `WithDeveloperCertificateTrust(false)`. Not run.

The dashboard's own transport is separate: it uses the dev certificate (or the unsecured-transport variables) whatever
the stack does; a localtest CA does not change it.

## 2. The certificate

**The dev certificate does not cover the stack.** This machine's dev certificate (version 6, `CN=localhost`) names
`localhost`, `*.dev.localhost`, `*.dev.internal`, `host.docker.internal`, `host.containers.internal`, `127.0.0.1`, `::1`.
`dotnet dev-certs https` has no option for more names. The stack needs `local.altinn.cloud` and `pdf.`,
`workflow-engine.`, `af.`, `app-frontend.` and `pgadmin.local.altinn.cloud` (the manifest's ingress hosts); a wildcard
`*.local.altinn.cloud` covers the five and any later one.

| Option | How | For | Against |
| --- | --- | --- | --- |
| **A. A localtest CA and a server certificate, made by the package** (recommended) | `CertificateRequest` in C#: an ECDSA P-256 CA (`BasicConstraints CA:true, pathlen 0`, `KeyCertSign`), and a leaf for `local.altinn.cloud`, `*.local.altinn.cloud`, `localhost`, `127.0.0.1` (`ServerAuth` EKU, signed by the CA). Made in the experiment: `openssl verify` OK, YARP serves it, curl and .NET and Node verify it with the CA | Contained: touches nothing outside the data directory until the developer trusts the CA; the CA stays stable, the leaf can be reissued without trusting again; the same files can serve studioctl | A trust step of its own per OS (below) |
| B. A dev-cert-shaped certificate with the extra SANs | The same `CertificateRequest` with `CN=localhost`, the ASP.NET dev-cert extension `1.3.6.1.4.1.311.84.1.1` = `06`, the dev certificate's SANs plus the stack's, imported with `dotnet dev-certs https --clean --import cert.pfx -p ...` and trusted with `--trust`. **Works** (isolated `HOME`): `IsHttpsDevelopmentCertificate: true`, `--trust` wrote the OpenSSL trust directory `~/.aspnet/dev-certs/trust` and the Chromium NSS database, and `--check --trust` reports it trusted with `SSL_CERT_DIR` set | Reuses `dotnet dev-certs`' cross-platform trust, and Aspire then uses it for the front door and container trust with no package code | `--clean` **replaces the developer's dev certificate for every project**; a later SDK with a newer dev-cert version, or `dotnet dev-certs https --clean`, removes the stack's names; a self-signed leaf is the trust anchor, so a reissue means trusting again |
| C. studioctl's certificate | studioctl has **no** TLS: no `dev-certs`, `tls` or `https` code in `src/cli` beyond devenv/kind's `pkg/cabundle` (a bundle of the sandbox's CAs for kind nodes and Flux, not a certificate maker). `certs.go` writes the access-token `.pfx` only | | Nothing to reuse |

**Storage and rotation (option A).** `<data>/tls/` beside `certs/` (`~/.altinn-app-aspire/tls/`): `ca.crt` (public,
`0644`), `ca.key` (`0600`), `server.crt`/`server.key` (`0600`; Aspire copies them into the front door, so they need not
be world-readable, unlike the access-token certificate that is bind-mounted). On start: create the CA if missing (10
years); reissue the leaf when it is missing, expires within 30 days, was signed by another CA, or lacks a SAN the
manifest's ingress hosts need; keep the leaf to 397 days or less (Apple's limit for TLS server certificates; whether it
applies to a user-trusted root was not checked). A name constraint on the CA (`permitted: local.altinn.cloud,
.local.altinn.cloud, localhost`) would limit the damage of a leaked `ca.key`; `CertificateRequest` has no builder for
it, so it needs an `AsnWriter`-encoded extension. Not tried.

**Trust in the browser and on the host.** The developer trusts `ca.crt` once:

| Platform | Browser | The app on the host (.NET) | Command (not run except Linux NSS) |
| --- | --- | --- | --- |
| Linux | Chromium and Chrome: the NSS database `~/.pki/nssdb`; Firefox: its profile's NSS database, or the enterprise-roots policy; snap-packaged browsers keep their own | OpenSSL: Aspire's `SSL_CERT_DIR` on the project resource covers the app it starts; another process needs `SSL_CERT_DIR` | `certutil -d sql:$HOME/.pki/nssdb -A -t "C,," -n altinn-localtest -i ca.crt` (per Firefox profile the same) |
| macOS | Safari and Chrome use the keychain; Firefox its own store unless `security.enterprise_roots.enabled` | The keychain, so trust is required for the app's `LocaltestValidation`, provisioning and token calls | `security add-trusted-cert -r trustRoot -k ~/Library/Keychains/login.keychain-db ca.crt` (asks for the password) |
| Windows | Edge and Chrome use the certificate store; Firefox imports enterprise roots by default | The certificate store | `X509Store(StoreName.Root, StoreLocation.CurrentUser).Add(ca)` (Windows asks for confirmation) or `certutil -user -addstore Root ca.crt` |

`dotnet dev-certs https --trust` on Linux has the same limits for its own certificate: it writes the NSS databases it
finds and `~/.aspnet/dev-certs/trust`, and .NET only trusts the latter with `SSL_CERT_DIR` set by the user; the command
says so ("trusted by some clients but not by others"). The package could offer a `trust` dashboard command (or a small
CLI step in the README) that does the per-OS step above; it cannot do it silently on macOS or Windows.

**This sandbox versus a developer machine.** Here every container gets `SSL_CERT_FILE`, `NODE_EXTRA_CA_CERTS`,
`CURL_CA_BUNDLE` and friends set to the mediated bundle by `/etc/containers/containers.conf`, and an OCI hook copies
the bundle over the image's `/etc/ssl/certs/ca-certificates.crt`. A developer's machine has none of that. The
experiments therefore ran with `--unsetenv SSL_CERT_FILE` (and `NODE_EXTRA_CA_CERTS`) to see the images' own trust.
Two consequences for a package on this sandbox: Aspire's `SSL_CERT_DIR` and the sandbox's `SSL_CERT_FILE` coexist
(verified, case D below), but a package that sets `NODE_EXTRA_CA_CERTS` on the BFF **replaces** the sandbox's value, so
the BFF would lose the mediated CA for outbound internet calls (it makes none in the stack as far as read).

## 3. Trust inside the containers

Every component calls the platform at `{{platform}}` through `--add-host local.altinn.cloud:host-gateway`, so under
https each one must trust the CA. Image bases (the vendored Dockerfiles, final stage):

| Images | Base | Trust with Aspire's `Append` (`SSL_CERT_DIR`) | Verified |
| --- | --- | --- | --- |
| storage | `aspnet:10.0-resolute-chiseled-extra` (no shell) | yes, .NET reads `SSL_CERT_DIR` and `SSL_CERT_FILE` | **run**: probe in the image, and in the scratch AppHost |
| receipt; access-management | `aspnet:10.0-resolute-chiseled`; `aspnet:10.0-noble-chiseled-extra` (no shell) | yes (same runtime) | no |
| authentication, authorization, register, resource-registry, events, notifications, profile | `aspnet:10.0-alpine` / `10.0.13-alpine3.24` | yes | no |
| correspondence, dialogporten webapi/graphql/service, dialogporten-adapter, workflow-engine | `aspnet:10.0.x` (Ubuntu) | yes | no |
| localtest app | `runtime-deps:10.0-alpine`, Native AOT | yes: AOT still uses the image's OpenSSL | **run**: probe (musl build) in the image |
| arbeidsflate-bff | `node:24.15.0-slim` | **no**: Node ignores `SSL_CERT_DIR` (`UNABLE_TO_VERIFY_LEAF_SIGNATURE`, also with `NODE_USE_SYSTEM_CA=1`, which reads one directory, not a list). `NODE_EXTRA_CA_CERTS` = Aspire's bundle works | **run**: in the image and in the scratch AppHost |
| arbeidsflate (nginx SPA) | `nginx:1.25-alpine` | not needed: it proxies http to the BFF | read |
| pdf3 worker (`ghcr.io/altinn/altinn-studio/runtime-pdf3-worker:tt02`) | Go on `chromedp/headless-shell`, `certutil` present | Chromium uses NSS, not `SSL_CERT_*`. The image's `docker-entrypoint.sh` already imports `STUDIO_CA_BUNDLE` (a PEM bundle) into `$HOME/.local/share/pki/nssdb`; so `STUDIO_CA_BUNDLE` = Aspire's bundle path. The Go side (`net/http`) reads `SSL_CERT_DIR` | entrypoint read from the image; import not run |
| Postgres, Redis | | no outbound https | |

Probe results (a `HttpClient` GET against a TLS server with the localtest certificate, run inside the image, sandbox
variables unset):

| Image | No CA | `SSL_CERT_DIR=/lt/certs:/etc/ssl/certs` | `SSL_CERT_FILE=<CA only>` |
| --- | --- | --- | --- |
| storage (chiseled) | `PartialChain` | OK, public site OK | OK, public site OK (.NET still reads the default directory) |
| localtest app (alpine, AOT runtime) | `PartialChain` | OK, public OK | OK, public OK |
| arbeidsflate-bff (Node 24) | `UNABLE_TO_VERIFY_LEAF_SIGNATURE` | same failure | `NODE_EXTRA_CA_CERTS=<CA>`: OK |

**No image needs a change**, so no `localtest:` deviation in a Dockerfile: the package sets
`WithCertificateAuthorityCollection(ca)` on every component, plus a `WithCertificateTrustConfiguration` callback that
sets `NODE_EXTRA_CA_CERTS` on Node images and `STUDIO_CA_BUNDLE` on pdf3, both to `ctx.CertificateBundlePath`. The
manifest needs to say which component needs which (a `caTrust: ["openssl", "node", "nss"]`-style field, or the package
keys on the image), so that studioctl can do the same with a bind mount and the same variables.

## 4. Termination

**TLS at the front door only.** The front door is the one public origin; inside, the components keep their http ports
and the routes (studioctl-server's `BoundTopologyIndex` even requires http destinations). End-to-end TLS would need a
certificate per component, Kestrel https in 20 images with different configuration, and gives nothing locally.
Kestrel cannot serve http and https on one port, so in https mode port 8000 is https only, and every URL that names it
changes together (the issuers in tokens must match what the components and the app fetch).

**What the components see.** The stock YARP image forwards `X-Forwarded-Proto: https`, `X-Forwarded-Host` and
`X-Forwarded-For` by default and keeps `Host` with `WithTransformUseOriginalHostHeader` (verified: an echo server behind
it saw `X-Forwarded-Proto: https`, `Host: local.altinn.cloud:8443`). No .NET component acts on it today (the
correction in the status block). `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` adds the forwarded-headers middleware
for `X-Forwarded-For` and `X-Forwarded-Proto` with every proxy trusted, before the app's own pipeline: verified on a
`CreateBuilder` and a `CreateSlimBuilder` app (the localtest app uses the slim builder), `Request.Scheme` becomes
`https` and `IsHttps` true. Set on every .NET component in https mode, it makes the 21b cookie policies give `Secure`
cookies and the scheme-built links right. Whether it interacts with Authentication's and the ServiceDefaults' own
`UseForwardedHeaders` calls was not run (both should see the header already consumed).

**The Arbeidsflate BFF** trusts the proxy (`trustProxy: true`) and its session cookie is `secure: 'auto'` (patch
0003), so behind the Aspire front door it sets `Secure` under https as in production; nginx passes the incoming
`X-Forwarded-Proto` through. studioctl-server's proxy adds no `X-Forwarded-*` (`PlatformTransformer`), so it would
need to.

**Absolute URLs.**

| From configuration (follow `{{platform}}`, nothing to do but the scheme) | From the request (right once forwarded headers are on) | Hard-coded http (must change) |
| --- | --- | --- |
| Authentication's issuer, discovery and `jwks_uri` (`GeneralSettings`, `OpenIdController`); the fake ID-porten issuer (`{PlatformEndpoint}localtest/api/idporten`, `IdPortenApi.cs:325`) and its `redirect_uri` check; the fake Maskinporten issuer; Dialogporten's issuer (`{platform}dialogporten`); Correspondence's issuers and OIDC authority; the adapter's URIs; the BFF's discovery (patch 0001 keeps the scheme); app-lib's platform base URL (from `ApiStorageEndpoint`) | Storage's continuation links (`InstancesController.cs:1470`, `#if LOCALTEST`), Receipt (`Program.cs:264`), Access Management's activity-log links, the Authorization fake's self-call (`LocaltestDecisionController.cs:74`) | `http://af.local.altinn.cloud:8000` in `authentication/fakes/LocaltestOidcClients.cs:28`, `localtest/Authentication/SeededClients.cs:22`, `dialogporten/fakes/GraphQL/LocaltestGraphQl.cs:17`, `correspondence/fakes/LocaltestCorrespondence.cs:45,72` (OIDC `redirect_uri` must match byte for byte; CORS origins). nginx's CSP also allows `https:`. The `http://local.altinn.cloud:8000/` defaults in the fakes' options are overridden by `{{platform}}` |

**The manifest.** It has no literal scheme: `{{platform}}` resolves to the topology's `PublicURL("app") + "/"` (Go
`placeholders.go`, `topology.go`; C# `Placeholders.cs`, `LocaltestTopology`), and the topology's scheme is fixed:
Go embeds `topology.yaml` with `ingress.scheme: http` and `NewLocal(port)` takes only the port; C# has
`LocaltestTopology.Default = new("http", 8000)` with a private constructor. So `{{platform}}` and every `{{url:*}}`
become https by making the scheme a runner input (Go `NewLocal(scheme, port)`, C# `new LocaltestTopology(scheme, port)`
from `AltinnLocaltestOptions.Https`), not by a manifest change. One Go side effect: `OTelURL()` also takes the ingress
scheme and would become `https://` for a collector that is not TLS; it needs its own scheme. The five Arbeidsflate
origins become a placeholder (`{{url:arbeidsflate}}`) passed as configuration.

## 5. What https changes

| Item | Under https |
| --- | --- |
| The 21b cookie policies (`LocaltestCookiePolicy`, Correspondence's `SameAsRequest`) | Inert once `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is set: cookies carry `Secure` as in production. Without it they still strip `Secure`, which still works in a browser. The code stays for http mode |
| app-lib's `keepAlive` (task 22, `cookies.md` row 19) | The gap disappears: the browser accepts the `Secure` cookie over https, so the ten-minute refresh works behind the stock YARP without any stripping. Task 22 is still needed for http mode and studioctl |
| `AppSettings__OpenIdWellKnownEndpoint`, `GeneralSettings__ExternalAppBaseUrl`, `PlatformSettings__Api*` | From the manifest's `app` section through `{{url:*}}`: https with the scheme input, no change to the section |
| The app process (on the host) calling the platform | Linux: Aspire's `SSL_CERT_DIR` on the project resource (not run with the app). macOS, Windows: the CA must be in the OS trust store (section 2) |
| app-lib's `LocaltestValidation` | Calls the version endpoint at the platform base URL derived from `ApiStorageEndpoint`, with a default `HttpClient`: works with the trust above |
| The PDF generator fetching the app | `PDF3_LOCALTEST_PUBLIC_BASE_URL` = `{{url:app}}` becomes https; its Chromium needs `STUDIO_CA_BUNDLE` (section 3) |
| Arbeidsflate | Its own host on https (the leaf's `*.local.altinn.cloud`), session cookie `Secure` through `secure: 'auto'`, the OIDC client's `redirect_uri` from configuration |
| `RequireHttpsMetadata = false`, `RequireHttps = false` (Correspondence's `IdportenTokenValidator`, Dialogporten's issuer cache, the fakes) | Harmless, no longer needed; stay for http mode |
| The smoke tests | `BASE=https://local.altinn.cloud:8000` plus `CURL_CA_BUNDLE=<data>/tls/ca.crt` (curl honours it, so no `-k`). 15 scripts hard-code `http://...local.altinn.cloud` somewhere beyond `BASE` (Arbeidsflate's and Dialogporten's `AF`, issuer assertions); each needs a look. Not run |
| The front door's own health check, the component health checks | Stay on http (the front door's `http` endpoint stays on a loopback port; the components' host ports are http) |
| Persistent containers | Switching https on or off, or a new CA, changes their environment and files, so Aspire recreates them; the data volumes stay |
| The dashboard | Unchanged (its transport is the dev certificate's question) |

What stays: the routes, the components' ports and health paths, the provisioning library (it takes the platform URI;
it runs in the AppHost process, which Aspire gives no `SSL_CERT_DIR`, so the package passes it an `HttpClient` whose
handler trusts the CA in code, `SslClientAuthenticationOptions` with an `X509ChainPolicy` custom trust store; not
tried), the access-token certificate, Postgres and Redis.

## 6. studioctl parity

studioctl-server **could** terminate TLS the same way: its Kestrel listener is `PlatformProxy.ConfigureKestrel`
(`ListenAnyIP` on 8000), and `ListenAnyIP(port, o => o.UseHttps(cert))` with the same `server.crt`/`server.key` is a
small change. It also needs an `X-Forwarded-Proto` header in `PlatformTransformer` (it sends none), the CA in every
component (`components.go`: bind-mount `ca.crt` read-only and set `SSL_CERT_DIR`, `NODE_EXTRA_CA_CERTS`,
`STUDIO_CA_BUNDLE` per the manifest's trust field; devenv's `cabundle` package shows the variables), the certificate
maker in Go (`crypto/x509`), the topology scheme, and `studioctl env trust`/docs. The Go loader's golden tests then need
an https case.

**Recommendation: Aspire first, behind an opt-in, with the manifest and the certificate layout designed for both.**
Aspire gets it cheapest (no image change, Aspire carries the files), and the owner asked about Aspire; the scheme as a
topology input and the trust field in the manifest keep studioctl a follow-up rather than a rewrite.

## 7. Experiments

All on this machine, 2026-10-22, each stopped and removed afterwards (`podman ps -a` and `ps` checked for `tools/dcp`,
the AppHost and the dashboard; nothing left).

1. **dev-certs state.** `dotnet dev-certs https --check --trust`: one dev certificate, version 6, valid to 2027-10-13,
   **not trusted**. Its SANs as listed in section 2.
2. **Certificate generation.** A single-file C# program with `CertificateRequest`: ECDSA P-256 CA and a leaf for the
   six ingress hosts, `localhost` and `127.0.0.1`; `openssl verify -CAfile ca.crt tls.crt`: OK.
3. **YARP container with TLS.** `mcr.microsoft.com/dotnet/nightly/yarp:2.3-preview` (entrypoint `dotnet
   /app/yarp.dll`, as the integration sets it), `ASPNETCORE_URLS=https://*:5001;http://*:5000`,
   `Kestrel__Certificates__Default__Path`/`KeyPath` on the mounted PEMs, one catch-all route to an echo server: `curl
   --cacert ca.crt https://local.altinn.cloud:8443/...` 200, `ssl_verify_result=0`, HTTP/2; `pdf.local.altinn.cloud`
   200; without the CA curl fails (`unable to get local issuer certificate`); a host outside the SANs fails on the
   name. The echo saw `X-Forwarded-Proto: https`, `X-Forwarded-Host`, the original `Host`.
4. **Trust in the images** (table in section 3), with a framework-dependent probe (`dotnet /probe/probe.dll` in the
   Storage image), a self-contained `linux-musl-arm64` probe in the localtest image (the glibc build cannot start
   there), and `node -e fetch(...)` in the BFF image, against `openssl s_server` with the leaf, reached through
   `--add-host local.altinn.cloud:host-gateway`.
5. **Scratch AppHost under Podman** (`Aspire.AppHost.Sdk@13.6.2`, `Aspire.Hosting.Yarp@13.6.2`, with
   `ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false` and the dashboard's http variables). The part
   that matters:

   ```csharp
   var cas = builder.AddCertificateAuthorityCollection("localtest-ca").WithCertificate(ca);
   var frontdoor = builder.AddYarp("frontdoor")
       .WithHttpsCertificate(server)            // X509Certificate2 with the private key
       .WithHostHttpsPort(8443)
       .WithHostPort(18441)                     // http moves off the https port
       .WithEndpoint("http", e => { e.IsProxied = false; e.TargetHost = "0.0.0.0"; })
       .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway")
       .WithConfiguration(/* one route to the echo server */);
   // The integration adds the https endpoint in its BeforeStartEvent handler; this one runs after it.
   builder.Eventing.Subscribe<BeforeStartEvent>((e, ct) =>
   {
       var https = frontdoor.Resource.Annotations.OfType<EndpointAnnotation>().Single(a => a.Name == "https");
       https.IsProxied = false;
       https.TargetHost = "0.0.0.0";
       return Task.CompletedTask;
   });
   builder.AddContainer("probe-storage", "ghcr.io/ivarne-agent/localtest-research/localtest-storage", "sha-98e2bae")
       /* probe entrypoint, --add-host local.altinn.cloud:host-gateway */
       .WithCertificateAuthorityCollection(cas).WaitFor(frontdoor);
   builder.AddContainer("probe-node", ".../localtest-arbeidsflate-bff", "sha-98e2bae")
       /* node -e fetch(...) */
       .WithCertificateAuthorityCollection(cas)
       .WithCertificateTrustConfiguration(ctx =>
       {
           ctx.EnvironmentVariables["NODE_EXTRA_CA_CERTS"] = ctx.CertificateBundlePath;
           return Task.CompletedTask;
       })
       .WaitFor(frontdoor);
   ```

   Results: the front door got `Kestrel__Certificates__Default__Path=/usr/lib/ssl/aspire/private/<thumbprint>.crt`,
   `KeyPath` likewise, `ASPNETCORE_URLS=https://*:5001;http://*:5000`, ports `0.0.0.0:8443->5001` and
   `0.0.0.0:18441->5000`. The Storage probe had `SSL_CERT_DIR=/usr/lib/ssl/aspire/certs:/etc/ssl/certs:/usr/local/share/ca-certificates:/etc/pki/tls/certs`
   and answered `OK 200 https://local.altinn.cloud:8443/...` (public site still OK); the Node probe had
   `NODE_EXTRA_CA_CERTS=/usr/lib/ssl/aspire/cert.pem` and answered `OK 200`, so DCP writes the bundle in `Append`
   mode too. curl from the host verified the chain. **First run without the event handler**: the https endpoint is
   proxied by default, so DCP listened on `127.0.0.1:8443` and the container's port was a random loopback port; curl
   from the host worked, but the containers got `Connection refused` through the host gateway. The package must make
   the https endpoint unproxied on `0.0.0.0`, as it does for http today; `WithEndpoint("https", ..., createIfNotExists:
   false)` called from the builder is a no-op because the endpoint does not exist until `BeforeStartEvent`. The
   handler order relies on Aspire running `BeforeStartEvent` subscribers in registration order.
6. **Forwarded headers.** A minimal web app, `curl -H 'X-Forwarded-Proto: https'`: without the variable `scheme=http`;
   with `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` `scheme=https isHttps=True`, for both `CreateBuilder` and
   `CreateSlimBuilder` (.NET 10).
7. **Option B.** In an isolated `HOME`: `dotnet dev-certs https --clean --import` of a dev-cert-shaped certificate
   with the stack's SANs is accepted (`IsHttpsDevelopmentCertificate: true`); `--trust` writes
   `~/.aspnet/dev-certs/trust` and `~/.pki/nssdb` and reports partial trust; with `SSL_CERT_DIR` including the trust
   directory, `--check --trust` says trusted. Aspire was not run against it.

A note for running AppHosts from a non-interactive shell: a background job ignores `SIGINT`, so `kill -INT` to the
`dotnet run` process group did nothing; `SIGTERM` to the AppHost process stopped it cleanly (DCP, dashboard and the
session containers went with it).

## Recommendation

1. **Termination at the front door**, components on http inside, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the
   .NET components in https mode, the YARP default `X-Forwarded-*` kept.
2. **Certificate option A**: the package makes a localtest CA and a leaf for `local.altinn.cloud`,
   `*.local.altinn.cloud`, `localhost`, `127.0.0.1` in `<data>/tls/`, reissues the leaf as needed, and the developer
   trusts the CA once (a `trust` command or documented per-OS steps). Not the dev certificate: it cannot carry the
   names, and replacing it (option B) reaches into every other project of the developer.
3. **Aspire first, opt-in**: `AltinnLocaltestOptions.Https` (default off until the trust step is documented and
   studioctl follows), turning the topology's scheme to https, `WithHttpsCertificate` + `WithHostHttpsPort(8000)` on the
   front door (its http endpoint moved to a loopback port for the health check), `WithCertificateAuthorityCollection`
   on every component with the Node and NSS callbacks, and `WithoutHttpsCertificate()` on the front door in http mode.
4. **studioctl next**, from the same manifest fields and certificate layout (section 6).

## Effort

| Piece | Days |
| --- | --- |
| Certificate maker and rotation in the package (CA, leaf, SAN check, tests) | 0.5 |
| Front door on https: the option, `WithHttpsCertificate`, the https endpoint unproxied on 8000, http moved, `WithoutHttpsCertificate` in http mode, model tests | 0.5 |
| CA trust on the components (collection, Node and pdf3 callbacks, the manifest's trust field and schema), forwarded-headers variable | 0.5 to 1 |
| Scheme as a topology input in Go and C#, the OTel URL kept http, golden tests for an https case; the Arbeidsflate origin as a placeholder in four fakes and the localtest app (image rebuilds) | 1 to 1.5 |
| Trust step per OS (a `trust` command or docs), README | 0.5 to 1 |
| Smoke tests runnable over https (`BASE`, `CURL_CA_BUNDLE`, the hard-coded URLs), a full run under Podman with the sample app in a browser | 1 to 1.5 |
| **Aspire total** | **4.5 to 6** |
| studioctl parity: Kestrel `UseHttps`, `X-Forwarded-Proto` in `PlatformTransformer`, CA mounts and variables in `components.go`, Go certificate maker, `env trust`, tests | 2 to 3 |

## Files a follow-up would change

- **Package** `localtest/aspire/Altinn.App.Aspire/src/`: `AltinnLocaltestOptions.cs` (`Https`), a new `LocaltestTls.cs`
  (CA and leaf), `LocaltestStack.cs` (front door https, CA collection on components, trust callbacks, forwarded-headers
  variable), `Manifest/LocaltestTopology.cs` (public constructor or factory with the scheme), `Manifest/Placeholders.cs`
  if a `{{url:arbeidsflate}}` is new, `AltinnAppExtensions.cs`/`AppProvisioning.cs` (nothing beyond the URI, unless
  macOS/Windows trust is checked at start), tests, README, the sample's `apphost.cs` comment.
- **Manifest** `localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, its schema and README: a
  per-component trust field; the Arbeidsflate origin placeholder in the components that need it.
- **studioctl** (parity): `internal/localtestv2/topology.yaml` and `topology.go` (scheme input, OTel scheme),
  `placeholders.go`, `internal/cmd/env/localtestv2/components.go` and `certs.go`, studioctl-server
  `Proxy/PlatformProxy.cs` and `Proxy/PlatformTransformer.cs`, the golden files.
- **Components**: `authentication/fakes/LocaltestOidcClients.cs`, `localtest/localtest/Authentication/SeededClients.cs`,
  `dialogporten/fakes/GraphQL/LocaltestGraphQl.cs`, `correspondence/fakes/LocaltestCorrespondence.cs` (origin from
  configuration).
- **Smoke tests**: `localtest/localtest/smoke-test.sh` and `localtest/components/*/smoke-test.sh` (15 scripts with
  hard-coded http URLs).
- **Docs**: `localtest/README.md` ("Run it"), `localtest/reviews/cookies.md` (the forwarded-headers correction and the
  https column), this folder's README; altinn-studio-docs only on adoption.

## Not verified

- The **full stack on https**: no component image ran behind the https front door, so token issuers, OIDC discovery,
  sign-in, the app, Arbeidsflate and the smoke tests over https are read, not run. The forwarded-headers variable was
  run on a minimal app, not in the components (nor its interplay with Authentication's and the ServiceDefaults'
  `UseForwardedHeaders`).
- The **pdf3 worker** importing `STUDIO_CA_BUNDLE` and its Chromium trusting the CA (entrypoint read only); whether
  headless-shell still trusts public roots (the CDN) with an NSS database holding only the localtest CA.
- **The app project** trusting the CA through Aspire's `SSL_CERT_DIR` on Linux (only containers were run), and on
  macOS and Windows through the OS store.
- **Browsers**: no browser loaded a page over https; the per-OS trust commands for macOS, Windows and Firefox are from
  documentation, only the Linux NSS import (through `dotnet dev-certs`) ran. The 397-day limit for a user-trusted root
  on macOS, and a name-constrained CA, were not tried.
- The **side finding** on machines with a trusted dev certificate (extra https endpoint, dev certificate in every
  container's trust) is read from Aspire's code; this machine's dev certificate is untrusted.
- Docker, rootless Podman, SELinux; the other component images' trust (same .NET runtime as the two run, so expected
  to behave alike).
