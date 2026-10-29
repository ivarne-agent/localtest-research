#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the storage component on the shared localtest Postgres. It does
# what upstream's infra/postgres_init.sql does (that script assumes it runs as a superuser named platform_storage_admin;
# here the superuser may be anyone, and localtest sets up its own users and databases): the roles platform_storage_admin
# (owner of storagedb and its storage schema, runs the Yuniql migrations at Storage startup) and platform_storage (the
# login role the host runs as), the grants and max_connections. The passwords come from the Postgres container's
# environment (PLATFORM_STORAGE_ADMIN_PASSWORD, PLATFORM_STORAGE_PASSWORD; the runtimes set them from their password
# files; psql reads them with \getenv, so no password is in an argument). Idempotent, so it can also be run by hand
# against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/storage/postgres-init/10-storage.sh
set -eu
: "${PLATFORM_STORAGE_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_storage_admin}"
: "${PLATFORM_STORAGE_PASSWORD:?is not set: the Postgres container needs it for the role platform_storage}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
\getenv admin_password PLATFORM_STORAGE_ADMIN_PASSWORD
\getenv password PLATFORM_STORAGE_PASSWORD
SELECT format('CREATE ROLE platform_storage_admin WITH LOGIN PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_storage_admin') \gexec
SELECT format('CREATE ROLE platform_storage WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_storage') \gexec
SELECT 'CREATE DATABASE storagedb OWNER platform_storage_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'storagedb') \gexec
ALTER SYSTEM SET max_connections TO '200';
SQL
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname storagedb <<'SQL'
CREATE SCHEMA IF NOT EXISTS storage AUTHORIZATION platform_storage_admin;
GRANT USAGE ON SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT ALL ON ALL SEQUENCES IN SCHEMA storage TO platform_storage;
SQL
