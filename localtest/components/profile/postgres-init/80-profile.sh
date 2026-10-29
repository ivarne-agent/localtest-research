#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the profile component on the shared localtest Postgres: the
# database and the two roles upstream's EF migrations expect. platform_profile_admin owns profiledb (not a superuser:
# the migrations only create schemas, tables and indexes in its own database and GRANT to platform_profile) and runs the
# migrations at start, as production's pre-upgrade hook does; platform_profile is the login role the host runs as. The
# passwords come from the Postgres container's environment (PLATFORM_PROFILE_ADMIN_PASSWORD, PLATFORM_PROFILE_PASSWORD;
# the runtimes set them from their password files; psql reads them with \getenv, so no password is in an argument).
# Idempotent, so it can also be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/profile/postgres-init/80-profile.sh
set -eu
: "${PLATFORM_PROFILE_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_profile_admin}"
: "${PLATFORM_PROFILE_PASSWORD:?is not set: the Postgres container needs it for the role platform_profile}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
\getenv admin_password PLATFORM_PROFILE_ADMIN_PASSWORD
\getenv password PLATFORM_PROFILE_PASSWORD
SELECT format('CREATE ROLE platform_profile_admin WITH LOGIN PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_profile_admin') \gexec
SELECT format('CREATE ROLE platform_profile WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_profile') \gexec
SELECT 'CREATE DATABASE profiledb OWNER platform_profile_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'profiledb') \gexec
SQL
