-- Table: register.person
-- BEFORE:
-- CREATE TABLE register.person(
--   uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   first_name text NOT NULL,
--   middle_name text,
--   last_name text NOT NULL,
--   address register.address,
--   mailing_address register.address,
--   date_of_birth date,
--   date_of_death date
-- )
-- TABLESPACE pg_default;
-- NOTE: Database is not yet in use, so we don't care that we drop columns here.
ALTER TABLE register.person
  DROP COLUMN address;

ALTER TABLE register.person
  DROP COLUMN mailing_address;

ALTER TABLE register.person
  ADD COLUMN address register.street_address;

ALTER TABLE register.person
  ADD COLUMN mailing_address register.mailing_address;

-- AFTER:
-- CREATE TABLE register.person(
--   uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   first_name text NOT NULL,
--   middle_name text,
--   last_name text NOT NULL,
--   address register.street_address,
--   mailing_address register.mailing_address,
--   date_of_birth date,
--   date_of_death date
-- )
-- TABLESPACE pg_default;
