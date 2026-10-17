# certs

Test-only key material for the stack. Nothing here is secret.

| File | Use |
| --- | --- |
| `localtest-accesstoken.pfx` | Password-less RSA key + self-signed certificate. Apps and platform components sign **platform access tokens** with it (`AccessTokenSettings__AccessTokenSigningCertificateFullPath`), exactly as they would with their Key Vault certificate in production. |
| `localtest-accesstoken.cer` | Its public certificate. The platform components' `IPublicSigningKeyProvider` fakes return it for every issuer, so the production `AccessTokenHandler` validates the tokens for real. |
| `localtest-accesstoken.key.pem` | The private key in PEM form, for tools. |

Regenerate with `openssl req -x509 -newkey rsa:2048 -nodes -keyout localtest-accesstoken.key.pem -out localtest-accesstoken.cer -days 3650 -subj '/CN=localtest-accesstoken'`
and `openssl pkcs12 -export -out localtest-accesstoken.pfx -inkey localtest-accesstoken.key.pem -in localtest-accesstoken.cer -passout pass:`.
