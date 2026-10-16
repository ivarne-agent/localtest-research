-- CREATE TABLE register.user (
--   id BIGSERIAL PRIMARY KEY NOT NULL,
--   uuid uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   user_id BIGINT NOT NULL,
--   is_active BOOLEAN NOT NULL DEFAULT TRUE
-- )
-- TABLESPACE pg_default;
ALTER TABLE register.user
  ADD COLUMN username TEXT;

ALTER TABLE register.user
  ADD CONSTRAINT only_active_user_username CHECK ((username IS NULL) OR (is_active = TRUE));

CREATE UNIQUE INDEX uq_user_username ON register.user (username) TABLESPACE pg_default
WHERE
  is_active = TRUE;

-- CREATE TABLE register.user (
--   id BIGSERIAL PRIMARY KEY NOT NULL,
--   uuid uuid NOT NULL REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE,
--   user_id BIGINT NOT NULL,
--   is_active BOOLEAN NOT NULL DEFAULT TRUE,
--   username TEXT
-- )
-- TABLESPACE pg_default;
