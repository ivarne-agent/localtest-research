#!/bin/sh
# Database for the workflow engine (the engine runs its own migrations at startup).
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -c 'CREATE DATABASE workflow_engine;'
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname workflow_engine -c 'CREATE EXTENSION IF NOT EXISTS pg_stat_statements;'
