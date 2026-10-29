# The shared Postgres for the localtest stack: one server, one database per component, created by the components' own
# init scripts (Postgres runs them on an empty data volume only; each is idempotent so it can be rerun by hand). No
# password is baked in: each script reads its roles' passwords from the container's environment (<ROLE>_PASSWORD, set by
# the runtimes from their password file, named in the manifest as {{password:<role>}}) and fails when one is missing.
# Build context: the localtest/ root, so the scripts come from the component folders they belong to:
#   docker build -f postgres/Dockerfile -t localtest-postgres .
# The image version follows studioctl (its workflow-engine database runs the same one).
FROM docker.io/library/postgres:18.3@sha256:7e32e9833a6fb1c92c32552794cb6ed569d51b445a54907d35fc112ef39684db
COPY components/storage/postgres-init/10-storage.sh /docker-entrypoint-initdb.d/10-storage.sh
COPY postgres-init/20-workflow-engine.sh /docker-entrypoint-initdb.d/20-workflow-engine.sh
COPY components/events/postgres-init/30-events.sh /docker-entrypoint-initdb.d/30-events.sh
COPY components/resource-registry/postgres-init/40-resource-registry.sh /docker-entrypoint-initdb.d/40-resource-registry.sh
COPY components/access-management/postgres-init/45-access-management.sh /docker-entrypoint-initdb.d/45-access-management.sh
COPY components/dialogporten/postgres-init/50-dialogporten.sh /docker-entrypoint-initdb.d/50-dialogporten.sh
COPY components/notifications/postgres-init/60-notifications.sh /docker-entrypoint-initdb.d/60-notifications.sh
COPY components/correspondence/postgres-init/70-correspondence.sh /docker-entrypoint-initdb.d/70-correspondence.sh
COPY components/profile/postgres-init/80-profile.sh /docker-entrypoint-initdb.d/80-profile.sh
COPY components/arbeidsflate/postgres-init/90-arbeidsflate.sh /docker-entrypoint-initdb.d/90-arbeidsflate.sh
# timezone and log_timezone UTC, as production's Postgres: upstream column defaults such as Events' and Profile's
# `now() at time zone 'utc'` are only right when the session time zone is UTC. TZ=UTC (the manifest's envs) makes initdb
# write UTC into a new data volume; the flags also set it on a volume created under the earlier Europe/Oslo setting.
CMD ["postgres", "-c", "shared_preload_libraries=pg_stat_statements", "-c", "timezone=UTC", "-c", "log_timezone=UTC"]
