#!/usr/bin/env bash
# Moves every research image of the localtest-v2 manifest to one build: rewrites each "tag": "sha-..." in
# studioctl/src/cli/internal/localtestv2/manifest/manifest.json to the given tag. Images on other tags (the PDF generator
# and the workflow engine at tt02, Redis is a research image and bumps with the rest) are left alone. The images workflow
# never writes the manifest: run this once a build is proven, then regenerate the golden snapshot it prints.
#
#   localtest/tools/bump-tag.sh sha-1a2b3c4
set -euo pipefail
tag=${1:?usage: bump-tag.sh sha-<commit>}
if [[ ! $tag =~ ^sha-[0-9a-f]{7,40}$ ]]; then
  echo "bump-tag.sh: $tag is not a sha-<commit> tag" >&2
  exit 2
fi
here=$(cd "$(dirname "$0")" && pwd)
manifest=$here/../studioctl/src/cli/internal/localtestv2/manifest/manifest.json
before=$(grep -cE '"tag": "sha-[0-9a-f]+"' "$manifest" || true)
if [[ $before -eq 0 ]]; then
  echo "bump-tag.sh: no sha- tag in $manifest" >&2
  exit 1
fi
sed -i.bak -E "s/\"tag\": \"sha-[0-9a-f]+\"/\"tag\": \"$tag\"/" "$manifest"
rm -f "$manifest.bak"
echo "Pinned $before images to $tag in ${manifest#"$here/../"}."
echo "Regenerate the golden snapshot and review it:"
echo "  cd localtest/studioctl/src/cli && STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/ && git diff --stat"
