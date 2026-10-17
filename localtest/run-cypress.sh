#!/usr/bin/env bash
# Run the app-frontend Cypress suite against this stack.
#
#   ./run-cypress.sh [cypress args...]     e.g. ./run-cypress.sh --spec test/e2e/integration/frontend-test/on-entry.ts
#
# Expects: the stack up (docker compose up -d), the monorepo at MONOREPO (default ../../altinn-studio relative to this
# repo's parent) with the frontend built (src/App/frontend/dist) and FRONTEND_DIST in .env pointing at it, and the
# test apps started with run-app.sh (frontend-test on 5005, component-library on 5006, signing-test on 5007 by default).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
MONOREPO=${MONOREPO:-$(cd "$HERE/../.." && pwd)/altinn-studio}
cd "$MONOREPO/src/App/frontend"
exec npx cypress run --env environment=localtest "$@"
