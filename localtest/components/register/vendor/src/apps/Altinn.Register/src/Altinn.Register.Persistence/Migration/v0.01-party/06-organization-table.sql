-- Table: register.organization
CREATE TABLE register.organization(
  uuid uuid PRIMARY KEY NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  unit_status text,
  unit_type text,
  telephone_number text,
  mobile_number text,
  fax_number text,
  email_address text,
  internet_address text,
  mailing_address register.address,
  business_address register.address
)
TABLESPACE pg_default;
