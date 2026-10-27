#!/usr/bin/env bash
# Run an Altinn app from a source checkout against the localtest stack and register it with the front door, which
# routes it and provisions it from the running app (localtest/README.md, "What the app runner pushes").
#
#   ./run-app.sh <path-to-app-repo> [port]        e.g. ./run-app.sh ~/code/altinn-studio/src/test/apps/frontend-test 5005
#
# The app listens on 0.0.0.0:<port> on this machine; the front door reaches it as host.docker.internal:<port>.
# Needs the localtest app as the front door (it registers through that app's own API, PUT localtest/api/apps), as under
# studioctl's localtest-v2 runtime since task 27a (where studioctl-server registers the apps `studioctl run` starts).
# Platform URLs point at http://local.altinn.cloud:8000 (override with PLATFORM or LOCALTEST_ORIGIN). Logs: .run/<org>-<app>.log, pid file next to it.
set -euo pipefail
APP_DIR=${1:?app directory}; PORT=${2:-5005}
PLATFORM=${PLATFORM:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
HERE=$(cd "$(dirname "$0")" && pwd)
META="$APP_DIR/App/config/applicationmetadata.json"
ORG=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["org"])' "$META")
APP=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"].split("/")[1])' "$META")
mkdir -p "$HERE/.run"; LOG="$HERE/.run/$ORG-$APP.log"; PIDF="$HERE/.run/$ORG-$APP.pid"
if [[ -f "$PIDF" ]] && kill -0 "$(cat "$PIDF")" 2>/dev/null; then kill "$(cat "$PIDF")"; fi
# Whatever still listens on the port (an app started outside this script, for example) must go before we bind it.
for p in $( (ss -ltnp 2>/dev/null || netstat -ltnp 2>/dev/null) | grep -E "[:.]$PORT " | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u); do kill "$p" 2>/dev/null; done
for i in $(seq 1 30); do (ss -ltn 2>/dev/null || netstat -ltn) | grep -qE "[:.]$PORT " || break; sleep 1; done

ASM=$(sed -nE "s/.*<AssemblyName>([^<]+)<.*/\1/p" "$APP_DIR/App/App.csproj" | head -1); ASM=${ASM:-App}
DLL=$(ls "$APP_DIR"/App/bin/Release/net*/"$ASM".dll 2>/dev/null | head -1 || true)
if [[ -z "$DLL" ]]; then echo "building $APP_DIR/App ..."; dotnet build "$APP_DIR/App/App.csproj" -c Release --nologo -v q; DLL=$(ls "$APP_DIR"/App/bin/Release/net*/"$ASM".dll | head -1); fi

export ASPNETCORE_ENVIRONMENT=Development
# The app signs platform access tokens with the shared test certificate (certs/README.md).
# Altinn.Common.AccessTokenClient 3.x reads the full path; 1.x (the v9 app backend today) concatenates
# <parent of cwd> + KeysFolder + FileName with no separators, so give it a relative traversal from the app folder.
export AccessTokenSettings__AccessTokenSigningCertificateFullPath="$HERE/certs/localtest-accesstoken.pfx"
AccessTokenSettings__AccessTokenSigningKeysFolder="/$(realpath --relative-to="$APP_DIR" "$HERE/certs")/"
export AccessTokenSettings__AccessTokenSigningKeysFolder
export AccessTokenSettings__AccessTokenSigningCertificateFileName="localtest-accesstoken.pfx"
export Kestrel__EndPoints__Http__Url="http://0.0.0.0:$PORT"
export AppSettings__OpenIdWellKnownEndpoint="$PLATFORM/authentication/api/v1/openid/"
export AppSettings__AppFrontendAssetBaseUrl="${APP_FRONTEND_ASSET_BASE_URL:-$PLATFORM/app-frontend}"
export GeneralSettings__ExternalAppBaseUrl="$PLATFORM/$ORG/$APP/"
export GeneralSettings__HostName="${PLATFORM#http://}"; GeneralSettings__HostName=${GeneralSettings__HostName%%:*}
export PlatformSettings__ApiStorageEndpoint="$PLATFORM/storage/api/v1/"
export PlatformSettings__ApiRegisterEndpoint="$PLATFORM/register/api/v1/"
export PlatformSettings__ApiProfileEndpoint="$PLATFORM/profile/api/v1/"
export PlatformSettings__ApiAuthenticationEndpoint="$PLATFORM/authentication/api/v1/"
export PlatformSettings__ApiAuthorizationEndpoint="$PLATFORM/authorization/api/v1/"
export PlatformSettings__ApiEventsEndpoint="$PLATFORM/events/api/v1/"
export PlatformSettings__ApiPdf2Endpoint="http://pdf.${PLATFORM#http://}/pdf"
export PlatformSettings__ApiNotificationEndpoint="$PLATFORM/notifications/api/v1/"
export PlatformSettings__ApiCorrespondenceEndpoint="$PLATFORM/correspondence/api/v1/"
export PlatformSettings__ApiAccessManagementEndpoint="$PLATFORM/accessmanagement/api/v1/"
export PlatformSettings__ApiWorkflowEngineEndpoint="http://workflow-engine.${PLATFORM#http://}/api/v1/"
# The app frontend's inbox links into the local Arbeidsflate, as studioctl run and the Aspire package set them (README.md,
# "What the app runner pushes"); an empty value in the caller's environment keeps the start page.
ARBEIDSFLATE="http://af.${PLATFORM#http://}"
export PlatformFrontendSettings__ArbeidsflateInboxUrl="${PlatformFrontendSettings__ArbeidsflateInboxUrl-$ARBEIDSFLATE/}"
export PlatformFrontendSettings__ArbeidsflateDialogUrl="${PlatformFrontendSettings__ArbeidsflateDialogUrl-$ARBEIDSFLATE/inbox/{dialogId}}"
export PlatformFrontendSettings__ArbeidsflateProfileUrl="${PlatformFrontendSettings__ArbeidsflateProfileUrl-$ARBEIDSFLATE/profile}"
export PlatformFrontendSettings__ArbeidsflatePartiesUrl="${PlatformFrontendSettings__ArbeidsflatePartiesUrl-$ARBEIDSFLATE/profile/parties}"
# The app itself in the background, so $! is its pid (not a subshell's) and nothing keeps this script's stdout open.
( cd "$APP_DIR/App" || exit 1; nohup dotnet "$DLL" > "$LOG" 2>&1 < /dev/null & echo $! > "$PIDF" )
for i in $(seq 1 60); do curl -sf -o /dev/null "http://127.0.0.1:$PORT/$ORG/$APP/api/v1/applicationmetadata" && break; sleep 1; done
echo "app $ORG/$APP on port $PORT (pid $(cat "$PIDF"), log $LOG), ready after ${i}s"

# One registration: the front door routes /{org}/{app}/ to the app (it reaches this machine as host.docker.internal) and
# provisions it from the app's own API, as Studio does on deploy; the answer is the provisioning result or the problem.
RESULT=$(mktemp)
code=$(curl -s -m 120 -o "$RESULT" -w '%{http_code}' -X PUT "$PLATFORM/localtest/api/apps/$ORG/$APP" \
  -H 'Content-Type: application/json' -d "{\"url\":\"http://host.docker.internal:$PORT\"}")
echo "registered $PLATFORM/$ORG/$APP/ -> host port $PORT: HTTP $code"
cat "$RESULT"; echo; rm -f "$RESULT"
