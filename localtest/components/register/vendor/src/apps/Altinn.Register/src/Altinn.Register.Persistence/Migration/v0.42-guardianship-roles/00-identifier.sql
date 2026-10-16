ALTER DOMAIN register.identifier
  DROP CONSTRAINT identifier_valid;

ALTER DOMAIN register.identifier
  ADD CONSTRAINT identifier_valid CHECK (value ~ '^[a-z][a-z0-9]+(?:-[a-z0-9]+)*$');

ALTER DOMAIN register.identifier
  ADD CONSTRAINT identifier_max_length CHECK (char_length(value) <= 64);
