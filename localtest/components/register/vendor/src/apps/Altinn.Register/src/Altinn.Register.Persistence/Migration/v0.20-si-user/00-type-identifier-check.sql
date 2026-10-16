-- CONSTRAINT type_identifier_check CHECK ((party_type = 'person'::register.party_type AND person_identifier IS NOT NULL AND organization_identifier IS NULL) OR (party_type = 'organization'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NOT NULL))

ALTER TABLE register.party
  DROP CONSTRAINT type_identifier_check;

ALTER TABLE register.party
  ADD CONSTRAINT type_identifier_check CHECK (
    (party_type = 'person'::register.party_type AND person_identifier IS NOT NULL AND organization_identifier IS NULL)
    OR
    (party_type = 'organization'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NOT NULL)
    OR
    (party_type = 'self-identified-user'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NULL)
  );
