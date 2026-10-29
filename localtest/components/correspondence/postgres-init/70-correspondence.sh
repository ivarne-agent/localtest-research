#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the correspondence component on the shared localtest Postgres:
# the role Correspondence connects as (login, not superuser) and its database, upstream's own name, owned by that role.
# The password comes from the Postgres container's environment (CORRESPONDENCE_PASSWORD; the runtimes set it from their
# password files; psql reads it with \getenv, so no password is in an argument). The host applies its EF migrations at
# start (Development, MigrateWithLock) as the owner: hstore is trusted, so the migration that creates it needs no
# superuser; pg_stat_statements, pg_cron and pgaudit are skipped by the migrations' own exception blocks. Hangfire
# creates its "hangfire" schema itself. Idempotent, so it can also be run by hand against an existing volume, where
# Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/correspondence/postgres-init/70-correspondence.sh
set -eu
: "${CORRESPONDENCE_PASSWORD:?is not set: the Postgres container needs it for the role correspondence}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
\getenv password CORRESPONDENCE_PASSWORD
SELECT format('CREATE ROLE correspondence WITH LOGIN NOSUPERUSER PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'correspondence') \gexec
SELECT 'CREATE DATABASE correspondence OWNER correspondence'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'correspondence') \gexec
SQL
