-- Table: register.person
CREATE TABLE register.person(
  uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  first_name text NOT NULL,
  middle_name text,
  last_name text NOT NULL,
  address register.address,
  mailing_address register.address,
  date_of_birth date,
  date_of_death date
)
TABLESPACE pg_default;
