CREATE TYPE register.self_identified_user_type AS ENUM (
  'legacy',
  'edu',
  'idporten-email'
);

CREATE TABLE register.self_identified_user (
  "uuid" uuid NOT NULL PRIMARY KEY REFERENCES register.party(uuid) ON DELETE CASCADE,
  "type" register.self_identified_user_type NOT NULL,
  email text COLLATE case_insensitive,

  CONSTRAINT chk_si_email CHECK (
    CASE
      WHEN "type" = 'idporten-email' THEN email IS NOT NULL
      ELSE email IS NULL
    END
  )
);

CREATE UNIQUE INDEX uq_si_email ON register.self_identified_user (email);
