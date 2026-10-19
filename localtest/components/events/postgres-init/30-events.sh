#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the events component on the shared localtest Postgres: the
# database and the two roles upstream's README asks for (platform_events_admin as superuser, platform_events as a login
# role, both with password "Password"). Events runs its own Yuniql migrations at startup. Idempotent, so it can also be
# run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest_postgres_1 sh < components/events/postgres-init/30-events.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE platform_events_admin WITH LOGIN SUPERUSER PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_events_admin exists';
END $$;
DO $$ BEGIN
  CREATE ROLE platform_events WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_events exists';
END $$;
SELECT 'CREATE DATABASE eventsdb OWNER platform_events_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'eventsdb') \gexec
SQL
