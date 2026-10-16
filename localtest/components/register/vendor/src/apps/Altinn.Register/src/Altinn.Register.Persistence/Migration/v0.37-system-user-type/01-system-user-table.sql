CREATE TABLE register.system_user(
    uuid uuid NOT NULL PRIMARY KEY REFERENCES register.party(uuid) ON DELETE CASCADE,
    "type" register.system_user_type NOT NULL
)
TABLESPACE pg_default;
