#!/usr/bin/env bash
# Smoke test of the shared cookie policy (fakes/Cookies/, in memory in Cookies.Smoke, no stack), of the OTLP helper
# (fakes/Otlp/, Otlp.Tests against a fake collector, no stack) and of the shared
# IMaskinportenService fake (fakes/Maskinporten/), compiled against Altinn.ApiClients.Maskinporten
# 10.1.0 into Maskinporten.Smoke and run against the running stack: a token from the localtest Maskinporten token endpoint,
# exchanged through production Authentication. UseAltinnTestOrg=true must give urn:altinn:org=ttd, false digdir.
# Needs the stack (Authentication reads altinncdn.no for the organisation code) and a NuGet cache or nuget.org.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
BASE=${1:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
HERE=$(cd "$(dirname "$0")" && pwd)
FAIL=0
# The cookie policy (fakes/Cookies/) in memory, no stack needed.
if dotnet run --project "$HERE/Cookies.Smoke"; then
  echo "ok   cookie policy: no Secure and no SameSite=None over http, unchanged over https"
else
  echo "FAIL cookie policy (fakes/Cookies/)"; FAIL=1
fi
# The OTLP helper (fakes/Otlp/) against a fake collector on a loopback port, no stack needed.
if dotnet test "$HERE/Otlp.Tests"; then
  echo "ok   OTLP helper: all three signals with OTEL_EXPORTER_OTLP_ENDPOINT, named by OTEL_SERVICE_NAME; nothing without it"
else
  echo "FAIL OTLP helper (fakes/Otlp/)"; FAIL=1
fi
for flow in "true ttd" "false digdir"; do
  read -r test_org org <<<"$flow"
  if dotnet run --project "$HERE/Maskinporten.Smoke" -- "$BASE" "$test_org" "$org"; then
    echo "ok   fake with UseAltinnTestOrg=$test_org exchanges to urn:altinn:org=$org"
  else
    echo "FAIL fake with UseAltinnTestOrg=$test_org (expected urn:altinn:org=$org)"; FAIL=1
  fi
done
[[ $FAIL == 0 ]] && echo "all ok" || { echo "some checks failed"; exit 1; }
