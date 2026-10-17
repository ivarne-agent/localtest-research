#!/usr/bin/env bash
# Mint a platform access token signed with the shared test certificate, the way Altinn.Common.AccessTokenClient
# does in an app or platform component. Usage: mint-platform-access-token.sh [issuer] [app] [lifetime-seconds]
#   issuer: "platform" for a platform component, or the org for an app (default: platform)
#   app:    the app claim urn:altinn:app, e.g. "storage" or "frontend-test" (default: localtest)
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ISSUER=${1:-platform}; APP=${2:-localtest}; TTL=${3:-300}
KEY="$HERE/localtest-accesstoken.key.pem"; CER="$HERE/localtest-accesstoken.cer"
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
# kid = x5t = base64url(SHA-1 of the DER certificate), what X509SecurityKey uses as KeyId.
X5T=$(openssl x509 -in "$CER" -outform DER | openssl dgst -sha1 -binary | b64url)
NOW=$(date +%s)
HEADER=$(printf '{"alg":"RS256","typ":"JWT","kid":"%s","x5t":"%s"}' "$X5T" "$X5T" | b64url)
PAYLOAD=$(printf '{"iss":"%s","urn:altinn:app":"%s","nbf":%d,"exp":%d,"iat":%d}' "$ISSUER" "$APP" "$((NOW-5))" "$((NOW+TTL))" "$NOW" | b64url)
SIG=$(printf '%s.%s' "$HEADER" "$PAYLOAD" | openssl dgst -sha256 -sign "$KEY" -binary | b64url)
printf '%s.%s.%s\n' "$HEADER" "$PAYLOAD" "$SIG"
