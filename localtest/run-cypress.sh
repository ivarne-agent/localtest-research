#!/usr/bin/env bash
# Run the app-frontend Cypress suite against this stack.
#
#   ./run-cypress.sh [cypress args...]     e.g. ./run-cypress.sh --spec test/e2e/integration/frontend-test/on-entry.ts
#
# Expects: the stack up (studioctl env up -r localtest-v2), the monorepo at MONOREPO (default: altinn-studio next to this
# repository's checkout) with the frontend built (src/App/frontend/dist), and the apps the specs use running: under
# studioctl with `studioctl run --dev-frontend` and the built frontend served on the port it expects with
# Access-Control-Allow-Origin (see README.md, "Run it"); with the localtest app as the front door, with run-app.sh.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
MONOREPO=${MONOREPO:-$(cd "$HERE/../.." && pwd)/altinn-studio}
cd "$MONOREPO/src/App/frontend"
exec npx cypress run --env environment=localtest "$@"
