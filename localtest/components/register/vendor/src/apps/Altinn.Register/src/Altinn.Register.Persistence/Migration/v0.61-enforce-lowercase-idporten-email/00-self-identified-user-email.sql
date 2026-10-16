UPDATE register.self_identified_user
SET email = lower(email)
WHERE email IS NOT NULL
  AND email COLLATE "default" <> lower(email) COLLATE "default";

ALTER TABLE register.self_identified_user
  ALTER COLUMN email TYPE text COLLATE "default";

ALTER TABLE register.self_identified_user
  ADD CONSTRAINT chk_si_email_lowercase CHECK (
    email IS NULL OR email = lower(email)
  );
