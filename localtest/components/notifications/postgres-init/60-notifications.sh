#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the notifications component on the shared localtest Postgres:
# the database and the two roles upstream's Yuniql migrations expect (platform_notifications_admin as superuser, which
# the migrations make the owner of every function and table, and platform_notifications as the login role the host
# runs as, both with password "Password"). The host runs the migrations itself at startup. Idempotent, so it can also
# be run by hand against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/notifications/postgres-init/60-notifications.sh
set -eu
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
DO $$ BEGIN
  CREATE ROLE platform_notifications_admin WITH LOGIN SUPERUSER PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_notifications_admin exists';
END $$;
DO $$ BEGIN
  CREATE ROLE platform_notifications WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'platform_notifications exists';
END $$;
SELECT 'CREATE DATABASE notificationsdb OWNER platform_notifications_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'notificationsdb') \gexec
SQL
