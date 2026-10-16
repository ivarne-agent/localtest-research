-- Table: register.party
CREATE TABLE register.party(
  uuid uuid PRIMARY KEY NOT NULL,
  id bigint NOT NULL,
  party_type register.party_type NOT NULL,
  name text NOT NULL,
  person_identifier register.person_identifier,
  organization_identifier register.organization_identifier,
  created timestamp with time zone NOT NULL,
  updated timestamp with time zone NOT NULL,
  CONSTRAINT type_identifier_check CHECK ((party_type = 'person'::register.party_type AND person_identifier IS NOT NULL AND organization_identifier IS NULL) OR (party_type = 'organization'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NOT NULL))
)
TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_party_uuid ON register.party(uuid) INCLUDE (uuid, id, party_type, name, person_identifier, organization_identifier) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_party_id ON register.party(id) INCLUDE (uuid, id, party_type, name, person_identifier, organization_identifier) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_person_identifier ON register.party(person_identifier) INCLUDE (uuid, id, party_type, name, person_identifier, organization_identifier) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_organization_identifier ON register.party(organization_identifier) INCLUDE (uuid, id, party_type, name, person_identifier, organization_identifier) TABLESPACE pg_default;
