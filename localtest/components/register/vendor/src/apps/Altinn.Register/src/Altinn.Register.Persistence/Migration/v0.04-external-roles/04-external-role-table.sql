-- Table: register.external_role
CREATE TABLE register.external_role(
  source register.party_source NOT NULL,
  identifier register.identifier NOT NULL,
  from_party uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  to_party uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  PRIMARY KEY (source, identifier, from_party, to_party),
  FOREIGN KEY (source, identifier) REFERENCES register.external_role_definition(source, identifier) ON DELETE CASCADE ON UPDATE CASCADE
)
TABLESPACE pg_default;
