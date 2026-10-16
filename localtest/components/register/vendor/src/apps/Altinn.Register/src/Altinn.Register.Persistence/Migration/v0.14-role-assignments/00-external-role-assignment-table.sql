-- Rename external_role to external_role_assignment

ALTER TABLE register.external_role
RENAME TO external_role_assignment;

-- Create new enum type for external role source
CREATE TYPE register.external_role_source AS ENUM(
  'npr', -- Folkeregisteret
  'ccr', -- Enhetsregisteret
  'aar' -- Arbeidsgiver- og arbeidstakerregisteret
);

-- Update external role tables to use new enum
ALTER TABLE register.external_role_assignment
  DROP CONSTRAINT external_role_source_identifier_fkey;

ALTER TABLE register.external_role_definition
  ALTER COLUMN "source"
    SET DATA TYPE register.external_role_source
    USING "source"::text::register.external_role_source;

ALTER TABLE register.external_role_assignment
  ALTER COLUMN "source"
    SET DATA TYPE register.external_role_source
    USING "source"::text::register.external_role_source;

ALTER TABLE register.external_role_assignment
  ADD CONSTRAINT external_role_assignment_source_identifier_fkey FOREIGN KEY ("source", identifier) REFERENCES register.external_role_definition("source", identifier) ON DELETE CASCADE ON UPDATE CASCADE;
