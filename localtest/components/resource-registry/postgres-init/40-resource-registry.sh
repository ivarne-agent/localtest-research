#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the resource-registry component on the shared localtest
# Postgres: the database, and the two roles upstream's Yuniql migrations grant to (platform_authorization_admin, the
# migrator, as superuser; platform_authorization, the application role). Access Management uses the same two roles
# (its 45-access-management.sh), so both scripts create them when missing, with the same passwords from the Postgres
# container's environment (PLATFORM_AUTHORIZATION_ADMIN_PASSWORD, PLATFORM_AUTHORIZATION_PASSWORD; the runtimes set them
# from the manifest). The registry runs its Yuniql migrations at startup. Idempotent, so it can also be run by hand
# against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/resource-registry/postgres-init/40-resource-registry.sh
set -eu
: "${PLATFORM_AUTHORIZATION_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_authorization_admin}"
: "${PLATFORM_AUTHORIZATION_PASSWORD:?is not set: the Postgres container needs it for the role platform_authorization}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres \
  -v admin_password="$PLATFORM_AUTHORIZATION_ADMIN_PASSWORD" -v password="$PLATFORM_AUTHORIZATION_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE platform_authorization_admin WITH LOGIN SUPERUSER PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_authorization_admin') \gexec
SELECT format('CREATE ROLE platform_authorization WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_authorization') \gexec
SELECT 'CREATE DATABASE resourceregistrydb OWNER platform_authorization_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'resourceregistrydb') \gexec
SQL
