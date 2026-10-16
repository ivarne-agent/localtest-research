-- Table: register.import_job
-- CREATE TABLE register.import_job(
--   id text PRIMARY KEY NOT NULL,
--   source_max bigint NOT NULL,
--   enqueued_max bigint NOT NULL,
--   processed_max bigint NOT NULL,
--   CONSTRAINT enqueued_max_less_than_or_equal_to_source_max CHECK (enqueued_max <= source_max),
--   CONSTRAINT processed_max_less_than_or_equal_to_enqueued_max CHECK (processed_max <= enqueued_max),
--   CONSTRAINT source_max_positive CHECK (source_max >= 0),
--   CONSTRAINT enqueued_max_positive CHECK (enqueued_max >= 0),
--   CONSTRAINT processed_max_positive CHECK (processed_max >= 0)
-- )
-- TABLESPACE pg_default;

ALTER TABLE register.import_job
  DROP CONSTRAINT enqueued_max_less_than_or_equal_to_source_max,
  DROP CONSTRAINT source_max_positive;

ALTER TABLE register.import_job
  ALTER COLUMN source_max DROP NOT NULL;

ALTER TABLE register.import_job
  ADD CONSTRAINT enqueued_max_less_than_or_equal_to_source_max CHECK (enqueued_max <= source_max OR source_max IS NULL),
  ADD CONSTRAINT source_max_positive CHECK (source_max >= 0 OR source_max IS NULL);

-- Table: register.import_job
-- CREATE TABLE register.import_job(
--   id text PRIMARY KEY NOT NULL,
--   source_max bigint NULL,
--   enqueued_max bigint NOT NULL,
--   processed_max bigint NOT NULL,
--   CONSTRAINT enqueued_max_less_than_or_equal_to_source_max CHECK (enqueued_max <= source_max OR source_max IS NULL),
--   CONSTRAINT processed_max_less_than_or_equal_to_enqueued_max CHECK (processed_max <= enqueued_max),
--   CONSTRAINT source_max_positive CHECK (source_max >= 0 OR source_max IS NULL),
--   CONSTRAINT enqueued_max_positive CHECK (enqueued_max >= 0),
--   CONSTRAINT processed_max_positive CHECK (processed_max >= 0)
-- )
-- TABLESPACE pg_default;
