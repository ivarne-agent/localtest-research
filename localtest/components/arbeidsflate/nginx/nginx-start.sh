#!/bin/sh
# localtest: the SPA image's command (Dockerfile.frontend); upstream's is /start.sh, which this ends with.
# nginx/nginx.conf proxies /api/ to the BFF by its network alias through a variable, so nginx resolves the name per
# request (cached 10 s) instead of once at start: the BFF's address changes whenever its container restarts. nginx's
# resolver directive needs an address, which is the container runtime's DNS server from /etc/resolv.conf.
set -eu
ns=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
case "$ns" in *:*) ns="[$ns]" ;; esac
echo "resolver ${ns:-127.0.0.11} valid=10s ipv6=off;" > /etc/nginx/conf.d/00-localtest-resolver.conf
# The CSP in nginx/nginx.conf names the default public origin; the stack's origin is the one DIALOGPORTEN_STREAM_URL
# (the runner's, {{origin}}/dialogporten/graphql/stream) is on.
origin=$(printf '%s' "${DIALOGPORTEN_STREAM_URL:-}" | sed -nE 's#^(https?://[^/]+).*#\1#p')
if [ -n "$origin" ]; then
  sed -i "s#http://local.altinn.cloud:8000#$origin#g" /etc/nginx/conf.d/default.conf
fi
exec /start.sh
