#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the correspondence component on the shared localtest Postgres: the
# role Correspondence connects as (login, not superuser, password "localtest" as the other components') and its database,
# upstream's own name, owned by that role. The host applies its EF migrations at start (Development, MigrateWithLock) as
# the owner: hstore is trusted, so the migration that creates it needs no superuser; pg_stat_statements, pg_cron and
# pgaudit are skipped by the migrations' own exception blocks. Hangfire creates its "hangfire" schema itself.
# Idempotent, so it can also be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/correspondence/postgres-init/70-correspondence.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE correspondence WITH LOGIN NOSUPERUSER PASSWORD 'localtest';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'correspondence exists';
END $$;
SELECT 'CREATE DATABASE correspondence OWNER correspondence'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'correspondence') \gexec
SQL
