#!/bin/sh
# Postgres init script (docker-entrypoint-initdb.d) for the notifications component on the shared localtest Postgres:
# the database and the two roles upstream's Yuniql migrations expect (platform_notifications_admin as superuser, which
# the migrations make the owner of every function and table, and platform_notifications as the login role the host runs
# as). The passwords come from the Postgres container's environment (PLATFORM_NOTIFICATIONS_ADMIN_PASSWORD,
# PLATFORM_NOTIFICATIONS_PASSWORD; the runtimes set them from their password files; psql reads them with \getenv, so no
# password is in an argument). The host runs the migrations itself at startup. Idempotent, so it can also be run by hand
# against an existing volume, where Postgres skips init scripts:
#   podman exec -i localtest-v2-postgres sh < components/notifications/postgres-init/60-notifications.sh
set -eu
: "${PLATFORM_NOTIFICATIONS_ADMIN_PASSWORD:?is not set: the Postgres container needs it for the role platform_notifications_admin}"
: "${PLATFORM_NOTIFICATIONS_PASSWORD:?is not set: the Postgres container needs it for the role platform_notifications}"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
\getenv admin_password PLATFORM_NOTIFICATIONS_ADMIN_PASSWORD
\getenv password PLATFORM_NOTIFICATIONS_PASSWORD
SELECT format('CREATE ROLE platform_notifications_admin WITH LOGIN SUPERUSER PASSWORD %L', :'admin_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_notifications_admin') \gexec
SELECT format('CREATE ROLE platform_notifications WITH LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_notifications') \gexec
SELECT 'CREATE DATABASE notificationsdb OWNER platform_notifications_admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'notificationsdb') \gexec
SQL
