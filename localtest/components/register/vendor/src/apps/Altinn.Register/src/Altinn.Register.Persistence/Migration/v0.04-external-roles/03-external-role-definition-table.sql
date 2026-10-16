-- Table: register.external_role_definition
CREATE TABLE register.external_role_definition(
  source register.party_source NOT NULL,
  identifier register.identifier NOT NULL,
  name register.translated_text NOT NULL,
  description register.translated_text NOT NULL,
  PRIMARY KEY (source, identifier)
)
TABLESPACE pg_default;
