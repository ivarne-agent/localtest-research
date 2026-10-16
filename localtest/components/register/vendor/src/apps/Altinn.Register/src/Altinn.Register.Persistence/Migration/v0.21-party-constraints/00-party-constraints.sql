-- CONSTRAINT type_identifier_check CHECK (
--    (party_type = 'person'::register.party_type AND person_identifier IS NOT NULL AND organization_identifier IS NULL)
--    OR
--    (party_type = 'organization'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NOT NULL)
--    OR
--    (party_type = 'self-identified-user'::register.party_type AND person_identifier IS NULL AND organization_identifier IS NULL)
--  );

ALTER TABLE register.party
  DROP CONSTRAINT type_identifier_check;

ALTER TABLE register.party
  ADD CONSTRAINT person_identifier_check CHECK (
    (person_identifier IS NULL) <> (party_type = 'person'::register.party_type)
  );

ALTER TABLE register.party
  ADD CONSTRAINT organization_identifier_check CHECK (
    (organization_identifier IS NULL) <> (party_type = 'organization'::register.party_type)
  );
