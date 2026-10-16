-- Table: register.party_source_ref
CREATE TABLE register.party_source_ref(
  party_uuid uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  source register.party_source NOT NULL,
  source_identifier text NOT NULL,
  source_created timestamp with time zone,
  source_updated timestamp with time zone,
  PRIMARY KEY (party_uuid, source, source_identifier)
)
TABLESPACE pg_default;
