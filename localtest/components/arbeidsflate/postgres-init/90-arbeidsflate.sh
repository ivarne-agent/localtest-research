#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the arbeidsflate component on the shared localtest Postgres: the
# role Arbeidsflate's BFF connects as (login, not superuser, password "localtest" as the other components') and its
# database, owned by that role. The BFF's container runs upstream's TypeORM migrations at every start as the owner (plain
# tables, sequences and indexes; no extension). Upstream's own name for the database is "dialogporten" (compose.yml,
# init-db.sh), which the Dialogporten component already uses on this server, hence "arbeidsflate".
# Idempotent, so it can also be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/arbeidsflate/postgres-init/90-arbeidsflate.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE arbeidsflate WITH LOGIN NOSUPERUSER PASSWORD 'localtest';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'arbeidsflate exists';
END $$;
SELECT 'CREATE DATABASE arbeidsflate OWNER arbeidsflate'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'arbeidsflate') \gexec
SQL
