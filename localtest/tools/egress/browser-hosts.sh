#!/usr/bin/env bash
# Lists the hosts a browser session requested, from Playwright traces, for the egress review: the browser is its own
# egress point, separate from the app process.
#
#   playwright-cli -s=egress open --browser chromium http://local.altinn.cloud:8000/
#   playwright-cli -s=egress tracing-start
#   ... the flow ...
#   playwright-cli -s=egress tracing-stop      # prints the .network file under .playwright-cli/traces/
#   localtest/tools/egress/browser-hosts.sh .playwright-cli/traces/*.network > browser-hosts.tsv
#
# Output: origin (scheme://host:port), resource type when the trace has one, status, count; sorted by origin. Takes
# .network files or trace .zip files (the network file inside is read). Requests that never left the browser (data:,
# blob:) are counted under their scheme.
set -euo pipefail

[[ $# -gt 0 ]] || {
  echo "usage: browser-hosts.sh TRACE.network|TRACE.zip..." >&2
  exit 2
}

for f in "$@"; do
  case $f in
  *.zip) unzip -p "$f" '*.network' ;;
  *) cat "$f" ;;
  esac
done | jq -r '
  select(.type == "resource-snapshot")
  | .snapshot
  | (.request.url | capture("^(?<o>[a-z]+:(//[^/?#]*)?)").o // "?") as $origin
  | [$origin, (._resourceType // "-"), ((.response.status // 0) | tostring)]
  | @tsv' |
  sort | uniq -c | awk -v OFS='\t' '{ c = $1; sub(/^ *[0-9]+ /, ""); print $0, c }'
