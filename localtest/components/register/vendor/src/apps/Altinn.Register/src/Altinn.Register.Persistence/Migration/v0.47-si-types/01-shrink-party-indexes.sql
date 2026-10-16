-- removes unneeded includes from indexes on party table
DROP INDEX register.uq_organization_identifier;
DROP INDEX register.uq_person_identifier;
DROP INDEX register.uq_party_id;
DROP INDEX register.uq_party_uuid;

CREATE UNIQUE INDEX uq_organization_identifier ON register.party (organization_identifier) WHERE organization_identifier IS NOT NULL;
CREATE UNIQUE INDEX uq_person_identifier ON register.party (person_identifier) WHERE person_identifier IS NOT NULL;
CREATE UNIQUE INDEX uq_party_id ON register.party (id);
