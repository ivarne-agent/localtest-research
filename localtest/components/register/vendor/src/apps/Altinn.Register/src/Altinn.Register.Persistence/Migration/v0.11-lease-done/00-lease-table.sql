-- Table: register.lease
-- CREATE TABLE register.lease(
--   id text PRIMARY KEY NOT NULL,
--   token uuid NOT NULL,
--   expires timestamp with time zone NOT NULL
-- )
-- TABLESPACE pg_default;

ALTER TABLE register.lease
  ADD COLUMN acquired timestamp with time zone;

ALTER TABLE register.lease
  ADD COLUMN released timestamp with time zone;

-- Table: register.lease
-- CREATE TABLE register.lease(
--   id text PRIMARY KEY NOT NULL,
--   token uuid NOT NULL,
--   expires timestamp with time zone NOT NULL,
--   acquired timestamp with time zone,
--   released timestamp with time zone
-- )
-- TABLESPACE pg_default;
