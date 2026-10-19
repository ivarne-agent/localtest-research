-- Localtest additions to eventsdb, run by LocaltestDatabaseStartup as platform_events_admin after the production
-- migrations, on every start. Idempotent.

-- events.trace_log is range-partitioned by year and upstream has created partitions up to 2026 only; without one for
-- the current year, saving a webhook response fails. Same naming as upstream (trace_log_y2026), so a later upstream
-- migration with CREATE TABLE IF NOT EXISTS is a no-op.
DO $$
DECLARE y int;
BEGIN
  FOR y IN EXTRACT(YEAR FROM now())::int .. EXTRACT(YEAR FROM now())::int + 1 LOOP
    EXECUTE format('CREATE TABLE IF NOT EXISTS events.trace_log_y%s PARTITION OF events.trace_log FOR VALUES FROM (%L) TO (%L)',
                   y, y || '-01-01', (y + 1) || '-01-01');
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON events.trace_log_y%s TO platform_events', y);
  END LOOP;
END $$;

-- Every webhook POST the capture handler saw (production has no such table). Kept apart from the production schema.
CREATE SCHEMA IF NOT EXISTS localtest;
CREATE TABLE IF NOT EXISTS localtest.delivery (
    id bigserial PRIMARY KEY,
    "time" timestamptz NOT NULL DEFAULT now(),
    event_id text,
    event_type text,
    kind text NOT NULL,
    endpoint text NOT NULL,
    mode text NOT NULL,
    request_headers jsonb NOT NULL,
    request_body text NOT NULL,
    response_status int,
    response_body text,
    duration_ms bigint NOT NULL,
    error text
);
CREATE INDEX IF NOT EXISTS delivery_event_id_idx ON localtest.delivery (event_id);
GRANT USAGE ON SCHEMA localtest TO platform_events;
GRANT SELECT, INSERT, DELETE, TRUNCATE ON localtest.delivery TO platform_events;
GRANT USAGE ON SEQUENCE localtest.delivery_id_seq TO platform_events;
