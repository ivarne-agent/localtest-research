-- Table: register.lease
CREATE TABLE register.lease(
  id text PRIMARY KEY NOT NULL,
  token uuid NOT NULL,
  expires timestamp with time zone NOT NULL
)
TABLESPACE pg_default;
