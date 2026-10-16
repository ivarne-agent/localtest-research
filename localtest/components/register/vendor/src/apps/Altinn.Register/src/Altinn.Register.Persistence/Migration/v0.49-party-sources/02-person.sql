

CREATE TYPE register.person_source AS ENUM (
	'npr' -- npr - (Norwegian) National Population Register - Folkeregisteret
);

ALTER TABLE register.person
  ADD COLUMN source register.person_source NOT NULL DEFAULT 'npr';

ALTER TABLE register.person
  ALTER COLUMN source DROP DEFAULT;
