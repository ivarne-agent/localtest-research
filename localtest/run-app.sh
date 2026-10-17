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
# Altinn.Common.AccessTokenClient 3.x reads the full path; 1.x (the v9 app backend today) concatenates
# <parent of cwd> + KeysFolder + FileName with no separators, so give it a relative traversal from the app folder.
export AccessTokenSettings__AccessTokenSigningCertificateFullPath="$HERE/certs/localtest-accesstoken.pfx"
export AccessTokenSettings__AccessTokenSigningKeysFolder="/$(realpath --relative-to="$APP_DIR" "$HERE/certs")/"
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
# Push the app's artefacts to the platform, as Studio does on deploy (see README.md, "What the app runner pushes").
ORGTOKEN=$(curl -s "$PLATFORM/Home/GetTestOrgToken?org=$ORG" | tr -d '"')
push() { # method url file
  local code; code=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORGTOKEN" -H 'Content-Type: application/json' -X "$1" "$2" --data-binary @"$3"); echo "$code"
}
# 1. Register: the test users the app defines (testData.json as is), or none.
if [[ -f "$APP_DIR/App/wwwroot/testData.json" ]]; then
  curl -s -o /dev/null -w "register testData.json: HTTP %{http_code}\n" -H 'Content-Type: application/json' -X PUT "$PLATFORM/register/api/localtest/apps/$ORG/$APP" --data-binary @"$APP_DIR/App/wwwroot/testData.json" || true
else
  curl -s -o /dev/null -w "register (no testData.json): HTTP %{http_code}\n" -X PUT "$PLATFORM/register/api/localtest/apps/$ORG/$APP" || true
fi
# 2. Storage: application metadata and texts through the production endpoints (POST, or PUT when it already exists).
code=$(push POST "$PLATFORM/storage/api/v1/applications?appId=$ORG/$APP" "$META")
[[ "$code" == "201" || "$code" == "200" ]] || code=$(push PUT "$PLATFORM/storage/api/v1/applications/$ORG/$APP" "$META")
echo "storage application metadata: HTTP $code"
for f in "$APP_DIR"/App/config/texts/resource.*.json; do
  [[ -f "$f" ]] || continue
  lang=$(basename "$f" .json); lang=${lang#resource.}
  code=$(push POST "$PLATFORM/storage/api/v1/applications/$ORG/$APP/texts" "$f")
  [[ "$code" == "201" || "$code" == "200" ]] || code=$(push PUT "$PLATFORM/storage/api/v1/applications/$ORG/$APP/texts/$lang" "$f")
  echo "storage texts $lang: HTTP $code"
done
# 3. Access Management keeps its own copy of parties, roles and packages: tell it the Register changed.
curl -s -o /dev/null -w "access-management refresh: HTTP %{http_code}\n" -X POST "$PLATFORM/accessmanagement/api/localtest/refresh" || true
