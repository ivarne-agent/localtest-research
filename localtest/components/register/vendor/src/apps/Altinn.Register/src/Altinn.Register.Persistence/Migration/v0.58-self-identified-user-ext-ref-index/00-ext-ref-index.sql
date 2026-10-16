CREATE UNIQUE INDEX IF NOT EXISTS uq_self_identified_user_ext_ref
ON register.self_identified_user (ext_ref);
