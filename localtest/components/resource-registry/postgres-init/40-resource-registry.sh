#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the resource-registry component on the shared localtest
# Postgres: the database, and the two roles upstream's Yuniql migrations grant to (platform_authorization_admin, the
# migrator, as superuser; platform_authorization, the application role), both with the password "localtest" that
# Access Management's own database step also sets, so whichever component gets there first leaves a working pair.
# The registry runs its Yuniql migrations at startup. Idempotent, so it can also be run by hand against an existing
# volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/resource-registry/postgres-init/40-resource-registry.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE platform_authorization_admin WITH LOGIN SUPERUSER PASSWORD 'localtest';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_authorization_admin exists';
END $$;
DO $$ BEGIN
  CREATE ROLE platform_authorization WITH LOGIN PASSWORD 'localtest';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_authorization exists';
END $$;
SELECT 'CREATE DATABASE resourceregistrydb OWNER platform_authorization_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'resourceregistrydb') \gexec
SQL
