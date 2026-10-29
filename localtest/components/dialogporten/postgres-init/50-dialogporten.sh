#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the dialogporten component on the shared localtest Postgres: the
# role Dialogporten connects as and its database, with the two extensions the first migration asks for created up front
# (both are trusted, so the owner could create them too; EF's HasPostgresExtension then finds them). The hosts run the
# migrations at start (the production DevelopmentMigratorHostedService), as the database owner. PostgreSQL 18's built-in
# uuidv7() is what one migration calls, so there is no shim. The password comes from the Postgres container's
# environment (DIALOGPORTEN_PASSWORD; the runtimes set it from their password files; psql reads it with \getenv, so no
# password is in an argument). Idempotent, so it can also be run by hand against an existing volume, where Postgres
# skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/dialogporten/postgres-init/50-dialogporten.sh
set -eu
: "${DIALOGPORTEN_PASSWORD:?is not set: the Postgres container needs it for the role dialogporten}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
\getenv password DIALOGPORTEN_PASSWORD
SELECT format('CREATE ROLE dialogporten WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'dialogporten') \gexec
SELECT 'CREATE DATABASE dialogporten OWNER dialogporten'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'dialogporten') \gexec
SQL
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname dialogporten <<'SQL'
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gin;
SQL
