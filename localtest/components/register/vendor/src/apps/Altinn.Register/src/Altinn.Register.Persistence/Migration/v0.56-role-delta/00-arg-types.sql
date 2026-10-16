CREATE TYPE register.arg_role_party_ref_person_name AS (
  first_name text,
  middle_name text,
  last_name text,
  short_name text,
  display_name text
);

-- We have 3 different ways we can reference a party:
-- 1. By a party UUID (if the party already exists, typically used internally)
-- 2. By a person identification number (if the party is a person)
--    In this case, we also might get the person's name and address, such that the
--    party can be created if it doesn't already exist.
-- 3. By an organization number (if the party is an organization)
CREATE TYPE register.arg_role_party_ref AS (
  party_uuid uuid, -- case 1.
  person_identifier register.person_identifier, -- case 2.
  organization_identifier register.organization_identifier, -- case 3.

  -- extra data for case 2
  person_name register.arg_role_party_ref_person_name,
  mailing_address register.mailing_address
);

CREATE TYPE register.arg_role_assignment AS (
  to_party register.arg_role_party_ref,
  identifier register.identifier
);
