CREATE TABLE register.username (
  username text COLLATE case_insensitive PRIMARY KEY NOT NULL,
  uuid uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  is_active boolean NOT NULL
)
TABLESPACE pg_default;

CREATE INDEX ix_username_uuid ON register.username (uuid) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_username_active_uuid ON register.username (uuid) TABLESPACE pg_default
WHERE
  is_active = TRUE;
