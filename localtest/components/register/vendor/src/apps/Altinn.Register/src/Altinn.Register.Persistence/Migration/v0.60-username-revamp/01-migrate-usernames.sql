-- We lock the "user" table to ensure that no concurrent updates to usernames can occur
-- while we are migrating the data to the new "username" table.
LOCK TABLE register."user" IN ACCESS EXCLUSIVE MODE;

INSERT INTO register.username (username, uuid, is_active)
SELECT username, uuid, is_active
FROM register."user"
WHERE
  is_active = TRUE
  AND username IS NOT NULL;

ALTER TABLE register."user"
  DROP COLUMN username;
