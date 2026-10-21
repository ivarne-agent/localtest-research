# certs

Test-only key material for the stack. Nothing here is secret.

| File | Use |
| --- | --- |
| `localtest-accesstoken.pfx` | Password-less RSA key + self-signed certificate. Apps and platform components sign **platform access tokens** with it (`AccessTokenSettings__AccessTokenSigningCertificateFullPath`), exactly as they would with their Key Vault certificate in production. |
| `localtest-accesstoken.cer` | Its public certificate. The platform components' `IPublicSigningKeyProvider` fakes return it for every issuer, so the production `AccessTokenHandler` validates the tokens for real. |
| `localtest-accesstoken.key.pem` | The private key in PEM form, for tools. |
| `mint-platform-access-token.sh` | Mints a platform access token with the key, as `Altinn.Common.AccessTokenClient` does: `mint-platform-access-token.sh [issuer] [app] [lifetime-seconds]`. |

studioctl's `localtest-v2` runtime embeds a byte-identical copy of the `.pfx`
(`../studioctl/src/cli/internal/cmd/env/localtestv2/assets/localtest-accesstoken.pfx`), writes it under its own data
directory and mounts it read-only at `/certs` in the components; `studioctl run` points the apps at the same file.
Regenerating the key means replacing that copy too.

Regenerate with `openssl req -x509 -newkey rsa:2048 -nodes -keyout localtest-accesstoken.key.pem -out localtest-accesstoken.cer -days 3650 -subj '/CN=localtest-accesstoken'`
and `openssl pkcs12 -export -out localtest-accesstoken.pfx -inkey localtest-accesstoken.key.pem -in localtest-accesstoken.cer -passout pass:`.
