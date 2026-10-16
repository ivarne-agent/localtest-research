ALTER TABLE register.self_identified_user
    ADD COLUMN ext_ref text;

ALTER TABLE register.self_identified_user ADD CONSTRAINT chk_si_ext_ref CHECK (
CASE
    WHEN (type = 'edu'::register.self_identified_user_type) THEN (ext_ref IS NOT NULL)
    ELSE (ext_ref IS NULL)
END)
NOT VALID;
