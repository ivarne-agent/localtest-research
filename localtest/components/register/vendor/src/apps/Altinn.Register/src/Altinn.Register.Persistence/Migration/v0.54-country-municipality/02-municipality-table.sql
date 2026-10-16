-- Table: register.municipality
CREATE TABLE register.municipality(
  "number" integer PRIMARY KEY NOT NULL,
  "name" text NOT NULL,
  "status" register.municipality_status NOT NULL
)
TABLESPACE pg_default;
