ALTER TABLE register.party
  ADD CONSTRAINT party_id_check CHECK ((id IS NULL) <> (party_type IN ('person', 'organization', 'self-identified-user')));

ALTER TABLE register.party
  ALTER COLUMN id DROP NOT NULL;
