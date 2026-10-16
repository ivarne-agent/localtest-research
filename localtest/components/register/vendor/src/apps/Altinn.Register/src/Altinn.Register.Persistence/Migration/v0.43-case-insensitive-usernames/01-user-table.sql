-- CREATE TABLE register."user" (
-- 	id bigserial NOT NULL,
-- 	uuid uuid NOT NULL,
-- 	user_id int8 NOT NULL,
-- 	is_active bool DEFAULT true NOT NULL,
-- 	username text NULL,
-- 	CONSTRAINT only_active_user_username CHECK (((username IS NULL) OR (is_active = true))),
-- 	CONSTRAINT user_pkey PRIMARY KEY (id),
-- 	CONSTRAINT user_uuid_fkey FOREIGN KEY (uuid) REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE
-- );

ALTER TABLE register."user"
  ALTER COLUMN username TYPE citext;
