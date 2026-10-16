-- Composite: register.co_mailing_address (private - implementation detail)
CREATE TYPE register.co_mailing_address AS (
  address text,
  postal_code text,
  city text
);

-- Composite: register.co_street_address (private - implementation detail)
CREATE TYPE register.co_street_address AS (
  municipal_number text,
  municipal_name text,
  street_name text,
  house_number text,
  house_letter text,
  postal_code text,
  city text
);

-- Domain: register.mailing_address
-- No checks at this time, but create a domain so they can be added later
CREATE DOMAIN register.mailing_address AS register.co_mailing_address;

-- Domain: register.street_address
-- No checks at this time, but create a domain so they can be added later
CREATE DOMAIN register.street_address AS register.co_street_address;
