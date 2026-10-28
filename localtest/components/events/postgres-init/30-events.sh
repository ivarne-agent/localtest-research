#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the events component on the shared localtest Postgres: the
# database and the two roles upstream's README asks for (platform_events_admin as superuser, platform_events as a login
# role). The passwords come from the Postgres container's environment (PLATFORM_EVENTS_ADMIN_PASSWORD,
# PLATFORM_EVENTS_PASSWORD; the runtimes set them from the manifest). Events runs its own Yuniql migrations at startup.
# Idempotent, so it can also be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/events/postgres-init/30-events.sh
set -eu
: "${PLATFORM_EVENTS_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_events_admin}"
: "${PLATFORM_EVENTS_PASSWORD:?is not set: the Postgres container needs it for the role platform_events}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres \
  -v admin_password="$PLATFORM_EVENTS_ADMIN_PASSWORD" -v password="$PLATFORM_EVENTS_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE platform_events_admin WITH LOGIN SUPERUSER PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_events_admin') \gexec
SELECT format('CREATE ROLE platform_events WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_events') \gexec
SELECT 'CREATE DATABASE eventsdb OWNER platform_events_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'eventsdb') \gexec
SQL
