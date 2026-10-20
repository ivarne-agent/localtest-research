#!/usr/bin/env bash
# Build the pdf3 worker image the stack runs (localtest-pdf3:dev) from an altinn-studio checkout, as studioctl's dev
# mode does (context src/Runtime/pdf3, Dockerfile.worker), and record which commit it came from: the image gets the
# label org.opencontainers.image.revision and pdf3 reports the commit as its service version.
#   ./build-pdf3.sh [altinn-studio checkout]      (default ~/code/altinn-studio)
#   podman image inspect localhost/localtest-pdf3:dev --format '{{index .Labels "org.opencontainers.image.revision"}}'
set -euo pipefail
REPO=${1:-$HOME/code/altinn-studio}
REV=$(git -C "$REPO" rev-parse HEAD)
DIRTY=$(git -C "$REPO" status --porcelain -- src/Runtime/pdf3 | head -1)
[[ -z "$DIRTY" ]] || { REV="$REV-dirty"; echo "warning: src/Runtime/pdf3 has local changes; tagging the revision $REV"; }
CTR=$(command -v podman || command -v docker)
"$CTR" build -t localtest-pdf3:dev \
  -f "$REPO/src/Runtime/pdf3/Dockerfile.worker" \
  --build-arg SERVICE_VERSION="$REV" \
  --label org.opencontainers.image.revision="$REV" \
  --label org.opencontainers.image.source=https://github.com/Altinn/altinn-studio \
  "$REPO/src/Runtime/pdf3"
echo "localtest-pdf3:dev built from $REV"
