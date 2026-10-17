#!/bin/sh
# Expand APP_ROUTES ("org/app=http://host:port,org2/app2=http://host:port2") into nginx locations.
T=/etc/nginx/templates/default.conf.template
routes=""
IFS=','; for entry in ${APP_ROUTES:-}; do
  key=${entry%%=*}; target=${entry#*=}
  [ -n "$key" ] && [ -n "$target" ] || continue
  routes="$routes    location /$key/ { proxy_pass $target; }\n"
done
# Write the generated lines into the template before envsubst runs.
awk -v r="$routes" '{ if ($0 ~ /#APP_ROUTES#/) printf "%s", r; else print }' "$T" > "$T.tmp" && mv "$T.tmp" "$T"
