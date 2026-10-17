#!/usr/bin/env bash
# Run an Altinn app from a source checkout against the localtest stack, register it, and route it through the ingress.
#
#   ./run-app.sh <path-to-app-repo> [port]        e.g. ./run-app.sh ~/code/altinn-studio/src/test/apps/frontend-test 5005
#
# The app listens on 0.0.0.0:<port> on this machine; the ingress reaches it as host.docker.internal:<port>.
# Platform URLs point at http://local.altinn.cloud:8000 (override with PLATFORM). Logs: .run/<org>-<app>.log, pid file next to it.
set -euo pipefail
APP_DIR=${1:?app directory}; PORT=${2:-5005}
PLATFORM=${PLATFORM:-http://local.altinn.cloud:8000}
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
export AccessTokenSettings__AccessTokenSigningCertificateFullPath="$HERE/certs/localtest-accesstoken.pfx"
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
export PlatformSettings__ApiPdf2Endpoint="http://pdf.local.altinn.cloud:8000/pdf"
export PlatformSettings__ApiNotificationEndpoint="$PLATFORM/notifications/api/v1/"
export PlatformSettings__ApiCorrespondenceEndpoint="$PLATFORM/correspondence/api/v1/"
export PlatformSettings__ApiAccessManagementEndpoint="$PLATFORM/accessmanagement/api/v1/"
export PlatformSettings__ApiWorkflowEngineEndpoint="http://workflow-engine.local.altinn.cloud:8000/api/v1/"
( cd "$APP_DIR/App" && nohup dotnet "$DLL" > "$LOG" 2>&1 & echo $! > "$PIDF" )
for i in $(seq 1 60); do curl -sf -o /dev/null "http://127.0.0.1:$PORT/$ORG/$APP/api/v1/applicationmetadata" && break; sleep 1; done
echo "app $ORG/$APP on port $PORT (pid $(cat "$PIDF"), log $LOG), ready after ${i}s"

# Route it through the ingress: one nginx location per app, picked up by a graceful reload (no restart).
mkdir -p "$HERE/ingress/app-routes"
printf 'location /%s/ { proxy_pass http://host.docker.internal:%s; }\n' "$ORG/$APP" "$PORT" > "$HERE/ingress/app-routes/$ORG-$APP.conf"
INGRESS=$(podman ps --format '{{.Names}}' 2>/dev/null | grep -E '^localtest[_-]ingress[_-]1$' || docker ps --format '{{.Names}}' | grep -E '^localtest[_-]ingress[_-]1$')
( podman exec "$INGRESS" nginx -s reload 2>/dev/null || docker exec "$INGRESS" nginx -s reload )
for i in $(seq 1 30); do curl -sf -o /dev/null "$PLATFORM/$ORG/$APP/api/v1/applicationmetadata" && break; sleep 1; done
echo "ingress routes $PLATFORM/$ORG/$APP/ -> host port $PORT (after ${i}s)"
# Register the app with the platform (test data from the app, metadata fetched on demand).
curl -s -o /dev/null -w "register: HTTP %{http_code}\n" -H 'Content-Type: application/json' -X POST "$PLATFORM/register/api/localtest/apps" -d "{\"org\":\"$ORG\",\"app\":\"$APP\"}" || true
