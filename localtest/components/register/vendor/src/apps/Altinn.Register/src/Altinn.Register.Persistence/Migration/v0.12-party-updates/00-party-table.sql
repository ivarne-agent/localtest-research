-- Sequence register.party_version_id_seq

CREATE SEQUENCE register.party_version_id_seq AS bigint;


-- Table: register.party
-- CREATE TABLE register.party (
-- 	uuid uuid NOT NULL,
-- 	id int8 NOT NULL,
-- 	party_type register.party_type NOT NULL,
-- 	name text NOT NULL,
-- 	person_identifier register.person_identifier NULL,
-- 	organization_identifier register.organization_identifier NULL,
-- 	created timestamptz NOT NULL,
-- 	updated timestamptz NOT NULL
-- );

ALTER TABLE register.party
  ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;

ALTER TABLE register.party
  ADD COLUMN version_id bigint NOT NULL DEFAULT nextval('register.party_version_id_seq');

ALTER TABLE register.party
  ADD CONSTRAINT uq_version_id UNIQUE (version_id);

-- CREATE TABLE register.party (
-- 	uuid uuid NOT NULL,
-- 	id int8 NOT NULL,
-- 	party_type register.party_type NOT NULL,
-- 	name text NOT NULL,
-- 	person_identifier register.person_identifier NULL,
-- 	organization_identifier register.organization_identifier NULL,
-- 	created timestamptz NOT NULL,
-- 	updated timestamptz NOT NULL,
--  is_deleted boolean NOT NULL DEFAULT false,
--  version_id int8 NOT NULL DEFAULT nextval('register.party_version_id_seq')
-- );

CREATE FUNCTION register.update_version_id()
RETURNS TRIGGER AS $BODY$
BEGIN
  NEW.version_id = nextval('register.party_version_id_seq');
  RETURN NEW;
END
$BODY$
LANGUAGE plpgsql;

CREATE TRIGGER update_party_version_id
BEFORE UPDATE ON register.party
FOR EACH ROW EXECUTE PROCEDURE register.update_version_id();
