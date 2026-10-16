-- Table: register.organization
-- BEFORE:
-- CREATE TABLE register.organization(
--   uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   unit_status text,
--   unit_type text,
--   telephone_number text,
--   mobile_number text,
--   fax_number text,
--   email_address text,
--   internet_address text,
--   mailing_address register.address,
--   business_address register.address
-- )
-- TABLESPACE pg_default;
-- NOTE: Database is not yet in use, so we don't care that we drop columns here.
ALTER TABLE register.organization
  DROP COLUMN mailing_address;

ALTER TABLE register.organization
  DROP COLUMN business_address;

ALTER TABLE register.organization
  ADD COLUMN mailing_address register.mailing_address;

ALTER TABLE register.organization
  ADD COLUMN business_address register.mailing_address;

-- AFTER:
-- CREATE TABLE register.organization(
--   uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   unit_status text,
--   unit_type text,
--   telephone_number text,
--   mobile_number text,
--   fax_number text,
--   email_address text,
--   internet_address text,
--   mailing_address register.mailing_address,
--   business_address register.mailing_address
-- )
-- TABLESPACE pg_default;
