ALTER TABLE register.person
  ADD COLUMN short_name text;

UPDATE register.person
  SET short_name = party.name
FROM register.party
WHERE person.uuid = party.uuid;

ALTER TABLE register.person
  ALTER COLUMN short_name SET NOT NULL;
