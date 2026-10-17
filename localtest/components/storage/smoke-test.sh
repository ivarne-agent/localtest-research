#!/bin/bash
# Standalone smoke test of the storage component: a fresh Postgres container (podman or docker), the register and
# authentication components, a stub on PDP_PORT that permits every PDP decision, and Storage on STORAGE_PORT. No ingress:
# Storage's PDP and Register URLs point at the processes directly. Application metadata and texts are pushed through
# the production endpoints (smoke/apps/ttd/testapp), as the app runner does.
# Usage: ./smoke-test.sh   (builds the three hosts first if they are not built; needs ports 5020, 8000, PG_PORT,
# PDP_PORT and STORAGE_PORT free, and local.altinn.cloud resolving to 127.0.0.1)
set -u
HERE=$(cd "$(dirname "$0")" && pwd); CO=$(dirname "$HERE")
A=$CO/authentication; R=$CO/register; S=$HERE
BASE=http://local.altinn.cloud:8000; PG_PORT=${PG_PORT:-5433}; PDP_PORT=${PDP_PORT:-5050}; STORAGE_PORT=${STORAGE_PORT:-5010}
ST=http://127.0.0.1:$STORAGE_PORT/storage/api/v1
WORK=$(mktemp -d); PG=lt-storage-smoke-pg; PIDS=()
CTR=$(command -v podman || command -v docker)
FAILED=0

cleanup() { [ ${#PIDS[@]} -gt 0 ] && kill "${PIDS[@]}" 2>/dev/null; "$CTR" rm -f $PG >/dev/null 2>&1; }
trap cleanup EXIT
check() { # name, result (0 = ok), detail
  if [ "$2" = 0 ]; then echo "PASS $1 ${3:-}"; else echo "FAIL $1 ${3:-}"; FAILED=1; fi
}
build() { # component dir, project dir, dll
  [ -f "$1/$2/bin/Release/net10.0/$3" ] && return
  (cd "$1" && DirectoryBuildPropsPath=$1/localtest.build.props DirectoryBuildTargetsPath=$1/localtest.build.targets \
    dotnet build -c Release --nologo -v q "$2" >/dev/null) || { echo "build of $2 failed"; exit 1; }
}
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval(sys.argv[1]))" "$1" 2>/dev/null; }

build $R vendor/src/apps/Altinn.Register/src/Altinn.Register Altinn.Register.dll
build $A vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication Altinn.Platform.Authentication.dll
build $S vendor/src/Storage Altinn.Platform.Storage.dll

echo "== fresh Postgres on $PG_PORT (postgres-init/10-storage.sh + vendored infra/postgres_init.sql)"
"$CTR" rm -f $PG >/dev/null 2>&1
"$CTR" run -d --name $PG -p $PG_PORT:5432 -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -v $S/postgres-init/10-storage.sh:/docker-entrypoint-initdb.d/10-storage.sh:ro,Z \
  -v $S/vendor/infra/postgres_init.sql:/storage-init/postgres_init.sql:ro,Z docker.io/library/postgres:16-alpine >/dev/null
for i in $(seq 1 60); do "$CTR" logs $PG 2>&1 | grep -q "PostgreSQL init process complete" && "$CTR" exec $PG pg_isready -q && break; sleep 1; done

cd $R/vendor/src/apps/Altinn.Register/src/Altinn.Register && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5020 \
  Localtest__Register__TestDataPath=$R/testdata/testData.json GeneralSettings__OpenIdWellKnownEndpoint=$BASE/authentication/api/v1/openid/ \
  exec dotnet bin/Release/net10.0/Altinn.Register.dll > $WORK/register.log 2>&1 & PIDS+=($!)
cd $A/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:8000 \
  GeneralSettings__PlatformEndpoint=$BASE/ Localtest__Authentication__RegisterLocaltestEndpoint=http://127.0.0.1:5020/register/api/localtest/ \
  exec dotnet bin/Release/net10.0/Altinn.Platform.Authentication.dll > $WORK/authn.log 2>&1 & PIDS+=($!)
python3 -I $S/smoke/stub.py $PDP_PORT > $WORK/stub.log 2>&1 & PIDS+=($!)
cd $S/vendor/src/Storage && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:$STORAGE_PORT \
  GeneralSettings__PlatformEndpoint=$BASE/ \
  PostgreSqlSettings__AdminConnectionString="Host=127.0.0.1;Port=$PG_PORT;Username=platform_storage_admin;Password={0};Database=storagedb" \
  PostgreSqlSettings__ConnectionString="Host=127.0.0.1;Port=$PG_PORT;Username=platform_storage;Password={0};Database=storagedb;Include Error Detail=True" \
  PlatformSettings__ApiAuthorizationEndpoint=http://127.0.0.1:$PDP_PORT/authorization/api/v1/ \
  RegisterServiceSettings__ApiRegisterEndpoint=http://127.0.0.1:5020/register/api/v1/ \
  Localtest__Storage__BlobRootPath=$WORK/blobs \
  exec dotnet bin/Release/net10.0/Altinn.Platform.Storage.dll > $WORK/storage.log 2>&1 & PIDS+=($!)
for i in $(seq 1 90); do
  curl -sf -o /dev/null http://127.0.0.1:$STORAGE_PORT/health && curl -sf -o /dev/null $BASE/health && curl -sf -o /dev/null http://127.0.0.1:5020/health && break
  sleep 1
done
check "storage, authentication and register healthy after ${i}s" $([ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$STORAGE_PORT/health)" = 200 ]; echo $?)

VERSION=$("$CTR" exec $PG psql -U postgres -d storagedb -tAc "select max(version) from public.__yuniql_schema_version where status='Successful'")
TABLES=$("$CTR" exec $PG psql -U postgres -d storagedb -tAc "select count(*) from pg_tables where schemaname='storage'")
check "migrations ran on the fresh database at startup" $([ -n "$VERSION" ] && [ "$TABLES" -gt 10 ]; echo $?) "(latest $VERSION, $TABLES tables)"

ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=altinn:serviceowner/instances.read")
USR=$(curl -s "$BASE/Home/GetTestUserToken?userId=1337")
check "service-owner and user tokens from the authentication component" $([ ${#ORG} -gt 100 ] && [ ${#USR} -gt 100 ]; echo $?)

CODE=$(curl -s -o /dev/null -w '%{http_code}' $ST/applications/ttd/testapp)
check "GET applications/ttd/testapp is 404 before the push" $([ "$CODE" = 404 ]; echo $?) "($CODE)"
FX=$S/smoke/apps/ttd/testapp
CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X POST "$ST/applications?appId=ttd/testapp" --data-binary @$FX/applicationmetadata.json)
T=$(curl -s $ST/applications/ttd/testapp | json "d['title']['nb']")
check "POST applications?appId=ttd/testapp (service-owner token as studio.designer), GET it from Postgres" $([ "$CODE" = 201 ] && [ "$T" = "Testapp (pushed)" ]; echo $?) "($CODE)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$ST/applications/ttd/testapp" --data-binary @$FX/applicationmetadata.json)
check "PUT applications/ttd/testapp (second push) is 200" $([ "$CODE" = 200 ]; echo $?) "($CODE)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X POST "$ST/applications/ttd/testapp/texts" --data-binary @$FX/texts/resource.nb.json)
T=$(curl -s -H "Authorization: Bearer $USR" $ST/applications/ttd/testapp/texts/nb | json "d['resources'][0]['value']")
check "POST applications/ttd/testapp/texts (200, production returns Ok), GET texts/nb back" $([ "$CODE" = 200 ] && [ "$T" = "Testapp (pushed)" ]; echo $?) "($CODE)"
L=$(curl -s $ST/applications/ttd | json "sorted(a['id'] for a in d['applications'])")
check "GET applications/ttd lists the pushed app" $([[ "$L" == *testapp* ]]; echo $?) "$L"

R=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$ST/instances?appId=ttd/testapp" \
  -d '{"instanceOwner":{"partyId":"500000"},"process":{"currentTask":{"elementId":"Task_1","altinnTaskType":"data"}}}')
ID=$(echo "$R" | head -n -1 | json "d['id']")
check "POST instances?appId=ttd/testapp for party 500000 as user 1337" $([ "$(echo "$R" | tail -1)" = 201 ] && [ -n "$ID" ]; echo $?) "$ID"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $USR" $ST/instances/$ID)
check "GET instances/{partyId}/{guid}" $([ "$CODE" = 200 ]; echo $?)

D=$(curl -s -H "Authorization: Bearer $USR" -H 'Content-Type: text/plain' -H 'Content-Disposition: attachment; filename=hello.txt' \
  --data-binary 'hello localtest' "$ST/instances/$ID/data?dataType=attachment")
DID=$(echo "$D" | json "d['id']")
BLOB=$(find $WORK/blobs -type f 2>/dev/null | head -1)
check "POST data?dataType=attachment writes a file blob" $([ -n "$DID" ] && [ -f "$BLOB" ]; echo $?) "${BLOB#$WORK/blobs/}"
B=$(curl -s -H "Authorization: Bearer $USR" $ST/instances/$ID/data/$DID)
check "GET data reads the blob back" $([ "$B" = "hello localtest" ]; echo $?)
SCAN=$(curl -s -H "Authorization: Bearer $USR" $ST/instances/$ID | json "[e.get('fileScanResult') for e in d['data']]")
check "file scan result Clean after the fake scan (enableFileScan data type)" $([ "$SCAN" = "['Clean']" ]; echo $?) "$SCAN"
Q=$(curl -s -H "Authorization: Bearer $ORG" "$ST/instances?org=ttd" | json "[i['id'] for i in d['instances']]")
check "GET instances?org=ttd with the service-owner token" $([[ "$Q" == *"$ID"* ]]; echo $?)

ERRORS=$(grep -ciE '^(fail|crit):|unhandled exception' $WORK/storage.log)
check "no errors in the storage log" $([ "$ERRORS" = 0 ]; echo $?) "($ERRORS)"
if [ $FAILED = 0 ]; then echo "ALL PASSED"; rm -rf "$WORK"; else echo "SOME FAILED; logs in $WORK"; exit 1; fi
