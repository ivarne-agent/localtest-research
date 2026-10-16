-- Domain: register.identifier
-- CREATE DOMAIN register.identifier AS text CONSTRAINT identifier_valid CHECK (value ~ '^[a-z]([a-z0-9_]{0,28}[a-z0-9])?$');
ALTER DOMAIN register.identifier
  DROP CONSTRAINT identifier_valid;

ALTER DOMAIN register.identifier
  ADD CONSTRAINT identifier_valid CHECK (value ~ '^[a-z]([a-z0-9-]{0,28}[a-z0-9])?$');

-- Domain: register.identifier
-- CREATE DOMAIN register.identifier AS text CONSTRAINT identifier_valid CHECK (value ~ '^[a-z]([a-z0-9-]{0,28}[a-z0-9])?$');
