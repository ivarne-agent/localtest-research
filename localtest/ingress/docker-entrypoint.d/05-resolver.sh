#!/bin/sh
# Use the container network's DNS (docker: 127.0.0.11, podman: the network gateway) unless NGINX_RESOLVER is set.
if [ -z "$NGINX_RESOLVER" ]; then
  ns=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
  sed -i "s|\${NGINX_RESOLVER}|${ns:-127.0.0.11}|g" /etc/nginx/templates/default.conf.template
fi
