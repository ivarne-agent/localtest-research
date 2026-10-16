-- Table: register.external_role_definition
-- CREATE TABLE register.external_role_definition(
--   source register.party_source NOT NULL,
--   identifier register.identifier NOT NULL,
--   name register.translated_text NOT NULL,
--   description register.translated_text NOT NULL,
--   PRIMARY KEY (source, identifier)
-- )
-- TABLESPACE pg_default;
ALTER TABLE register.external_role_definition
  ADD COLUMN code text;

CREATE UNIQUE INDEX uq_external_role_definition_code
  ON register.external_role_definition (code);

UPDATE
  register.external_role_definition
SET
  code = 'BEDR'
WHERE
  source = 'ccr'
  AND identifier = 'bedr';

UPDATE
  register.external_role_definition
SET
  code = 'AAFY'
WHERE
  source = 'ccr'
  AND identifier = 'aafy';

-- CREATE TABLE register.external_role_definition(
--   source register.party_source NOT NULL,
--   identifier register.identifier NOT NULL,
--   name register.translated_text NOT NULL,
--   description register.translated_text NOT NULL,
--   code text,
--   PRIMARY KEY (source, identifier)
-- )
-- TABLESPACE pg_default;
