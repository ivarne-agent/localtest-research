#!/usr/bin/env bash
# The app-frontend Cypress specs that once failed because of the platform (not the frontend), as a quick regression
# check of the stack. Starts the apps they need with run-app.sh, runs the specs with the spec reporter, stops the apps.
#   ./run-platform-regression.sh [altinn-studio checkout]     (default ~/code/altinn-studio)
#   ./run-platform-regression.sh --no-apps [checkout]         the apps already run, for example with `studioctl run`
#                                                            behind the localtest-v2 runtime (run-app.sh needs the
#                                                            localtest app as the front door)
#
# frontend-test/on-entry.ts               the first spec that passed: sign-in, the Register seed, Profile, authorized parties
# frontend-test/hash-router-redirect.ts   Storage's paging next link (LOCALTEST directive in InstancesController)
# frontend-test/self-identified-user.ts   Register's self-identified (ID-porten e-mail) users
# frontend-test/party-selection.ts        authorized parties and lower-case role codes from Access Management
# signering-brukerstyrt/signing.ts        instance delegations through Access Management; null lists in testData.json
# process-transition-test/workflow-status.ts  Events publishing (without it the workflow engine throttled the app)
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
START_APPS=1
if [[ "${1:-}" == --no-apps ]]; then START_APPS=0; shift; fi
STUDIO=${1:-$HOME/code/altinn-studio}
APPS=(frontend-test signering-brukerstyrt process-transition-test)
SPECS=(frontend-test/on-entry.ts frontend-test/hash-router-redirect.ts frontend-test/self-identified-user.ts
  frontend-test/party-selection.ts signering-brukerstyrt/signing.ts process-transition-test/workflow-status.ts)
if [[ $START_APPS == 1 ]]; then
  port=5005
  for app in "${APPS[@]}"; do "$HERE/run-app.sh" "$STUDIO/src/test/apps/$app" "$port" > /dev/null || exit 1; port=$((port + 1)); done
else
  echo "using the running apps: ${APPS[*]}"
fi
spec_list=$(printf 'test/e2e/integration/%s,' "${SPECS[@]}")
mkdir -p "$HERE/.run/cypress"
"$HERE/run-cypress.sh" --reporter spec --spec "${spec_list%,}" 2>&1 | tee "$HERE/.run/cypress/platform-regression.log"
status=${PIPESTATUS[0]}
[[ $START_APPS == 1 ]] && "$HERE/stop-apps.sh" > /dev/null
exit "$status"
