#!/bin/sh
# Database for the workflow engine (the engine runs its own migrations at startup).
set -eu
# Idempotent, so the script can be rerun by hand against an existing volume.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<'SQL'
SELECT 'CREATE DATABASE workflow_engine' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'workflow_engine')\gexec
SQL
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname workflow_engine -c 'CREATE EXTENSION IF NOT EXISTS pg_stat_statements;'
