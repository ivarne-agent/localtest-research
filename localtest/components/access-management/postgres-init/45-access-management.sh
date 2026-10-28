#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the access-management component on the shared localtest
# Postgres: the production roles platform_authorization_admin and platform_authorization, which Access Management's
# migrations grant to by name (shared with the Resource Registry, whose 40-resource-registry.sh creates them too), and
# the database authorizationdb owned by the admin, with hstore. Both roles are superusers here: in localtest mode the
# EF migrations run on the application connection and create schemas, extensions and grants, which the production split
# of privileges does not allow. The passwords come from the Postgres container's environment
# (PLATFORM_AUTHORIZATION_ADMIN_PASSWORD, PLATFORM_AUTHORIZATION_PASSWORD; the runtimes set them from the manifest).
# Access Management runs its production Init() (migrations, static data, Register import) at every start. Idempotent,
# so it can also be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/access-management/postgres-init/45-access-management.sh
set -eu
: "${PLATFORM_AUTHORIZATION_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_authorization_admin}"
: "${PLATFORM_AUTHORIZATION_PASSWORD:?is not set: the Postgres container needs it for the role platform_authorization}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres \
  -v admin_password="$PLATFORM_AUTHORIZATION_ADMIN_PASSWORD" -v password="$PLATFORM_AUTHORIZATION_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE platform_authorization_admin WITH LOGIN SUPERUSER PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_authorization_admin') \gexec
SELECT format('CREATE ROLE platform_authorization WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_authorization') \gexec
ALTER ROLE platform_authorization_admin WITH SUPERUSER;
ALTER ROLE platform_authorization WITH SUPERUSER;
SELECT 'CREATE DATABASE authorizationdb OWNER platform_authorization_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'authorizationdb') \gexec
SQL
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname authorizationdb \
  -c 'CREATE EXTENSION IF NOT EXISTS hstore;'
