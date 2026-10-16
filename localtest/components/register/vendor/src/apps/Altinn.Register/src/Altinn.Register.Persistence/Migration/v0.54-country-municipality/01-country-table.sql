-- Table: register.country
CREATE TABLE register.country(
  code2 text PRIMARY KEY NOT NULL,
  code3 text NOT NULL,
  "name" text NOT NULL
)
TABLESPACE pg_default;

CREATE UNIQUE INDEX uq_country_code3
  ON register.country(code3)
  TABLESPACE pg_default;
