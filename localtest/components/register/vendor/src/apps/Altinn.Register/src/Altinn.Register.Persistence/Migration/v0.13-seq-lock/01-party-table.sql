CREATE OR REPLACE FUNCTION register.update_version_id()
RETURNS TRIGGER AS $BODY$
BEGIN
  NEW.version_id = register.tx_nextval('register.party_version_id_seq');
  RETURN NEW;
END
$BODY$
LANGUAGE plpgsql;

ALTER TABLE register.party
  ALTER COLUMN version_id SET DEFAULT register.tx_nextval('register.party_version_id_seq');
