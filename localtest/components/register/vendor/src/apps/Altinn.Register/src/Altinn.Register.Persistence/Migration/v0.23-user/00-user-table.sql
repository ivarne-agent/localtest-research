-- Table: register.user

-- note: The id column is an internal identifier for the user in the register system. It only exists to be a primary
-- key for the user table. It should never be exposed to the outside world. The uuid column is what ties the user to
-- a party, but given that the user table contains historical user data, the uuid column is not a unique identifier,
-- and cannot be used as the primary key. The user_id column is Altinn 2 user id.
CREATE TABLE register.user (
  id BIGSERIAL PRIMARY KEY NOT NULL,
  uuid uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
  user_id BIGINT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
)
TABLESPACE pg_default;

CREATE INDEX ix_user_uuid ON register.user (uuid) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_user_id ON register.user (user_id) INCLUDE (uuid) TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_user_active_uuid ON register.user (uuid) TABLESPACE pg_default
WHERE
  is_active = TRUE;
