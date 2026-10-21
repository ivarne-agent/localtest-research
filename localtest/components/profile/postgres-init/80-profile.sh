#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the profile component on the shared localtest Postgres: the
# database and the two roles upstream's EF migrations expect. platform_profile_admin owns profiledb (not a superuser:
# the migrations only create schemas, tables and indexes in its own database and GRANT to platform_profile) and runs the
# migrations at start, as production's pre-upgrade hook does; platform_profile is the login role the host runs as. Both
# have the password "Password", as in upstream's appsettings.json. Idempotent, so it can also be run by hand against an
# existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/profile/postgres-init/80-profile.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE platform_profile_admin WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_profile_admin exists';
END $$;
DO $$ BEGIN
  CREATE ROLE platform_profile WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_profile exists';
END $$;
SELECT 'CREATE DATABASE profiledb OWNER platform_profile_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'profiledb') \gexec
SQL
