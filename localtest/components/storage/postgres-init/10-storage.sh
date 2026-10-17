#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the storage component on the shared localtest Postgres.
# Upstream runs infra/postgres_init.sql as the container superuser named platform_storage_admin; here the superuser
# may be anyone, so the admin role is created first, the vendored script runs unchanged, and the database and schema
# are handed to the admin role that runs the migrations at Storage startup.
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE platform_storage_admin WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_storage_admin exists';
END $$;
SQL
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -f /storage-init/postgres_init.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname storagedb <<'SQL'
ALTER DATABASE storagedb OWNER TO platform_storage_admin;
ALTER SCHEMA storage OWNER TO platform_storage_admin;
SQL
