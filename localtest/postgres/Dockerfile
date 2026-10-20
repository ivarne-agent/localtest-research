# The shared Postgres for the localtest stack: one server, one database per component, created by the components' own
# init scripts (Postgres runs them on an empty data volume only; each is idempotent so it can be rerun by hand).
# Build context: the localtest/ root, so the scripts come from the component folders they belong to:
#   docker build -f postgres/Dockerfile -t localtest-postgres .
# The image version follows studioctl (its workflow-engine database runs the same one).
FROM docker.io/library/postgres:18.3@sha256:7e32e9833a6fb1c92c32552794cb6ed569d51b445a54907d35fc112ef39684db
COPY components/storage/postgres-init/10-storage.sh /docker-entrypoint-initdb.d/10-storage.sh
COPY components/storage/vendor/infra/postgres_init.sql /storage-init/postgres_init.sql
COPY postgres-init/20-workflow-engine.sh /docker-entrypoint-initdb.d/20-workflow-engine.sh
COPY components/events/postgres-init/30-events.sh /docker-entrypoint-initdb.d/30-events.sh
COPY components/resource-registry/postgres-init/40-resource-registry.sh /docker-entrypoint-initdb.d/40-resource-registry.sh
CMD ["postgres", "-c", "shared_preload_libraries=pg_stat_statements"]
