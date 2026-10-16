CREATE OR REPLACE FUNCTION register.set_username(
  IN p_uuid register.party.uuid%TYPE,
  INOUT p_username register.username.username%TYPE
)
AS $$
DECLARE
  o_username register.username%ROWTYPE;
BEGIN
  -- Get existing active username by UUID if it exists
  SELECT u.*
  INTO o_username
  FROM register.username u
  WHERE u.uuid = p_uuid
    AND u.is_active = TRUE
  ORDER BY u.username -- Ensure locks are acquired in a consistent order to prevent deadlocks
  FOR UPDATE;

  -- If the username is already set to the requested value, return it
  IF FOUND AND o_username.username IS NOT DISTINCT FROM p_username THEN
    p_username := o_username.username;
    RETURN;
  END IF;

  -- If the requested username is NULL, deactivate the current username
  IF p_username IS NULL THEN
    IF FOUND THEN
      UPDATE register.username u
      SET is_active = FALSE
      WHERE u.username = o_username.username;
    END IF;

    RETURN;
  END IF;

  -- Deactivate the current username before activating another for the same UUID
  IF FOUND THEN
    UPDATE register.username u
    SET is_active = FALSE
    WHERE u.username = o_username.username;
  END IF;

  BEGIN
    INSERT INTO register.username (username, uuid, is_active)
    VALUES (p_username, p_uuid, TRUE)
    RETURNING * INTO o_username;
  EXCEPTION
    WHEN unique_violation THEN
      -- Reuse a previously held username for the same party, but keep normal conflicts for other parties
      SELECT u.*
      INTO o_username
      FROM register.username u
      WHERE u.username = p_username
      FOR UPDATE;

      IF NOT FOUND OR o_username.uuid <> p_uuid THEN
        RAISE;
      END IF;

      -- Mark the current active username as inactive
      UPDATE register.username u
      SET is_active = FALSE
      WHERE u.uuid = p_uuid
        AND u.is_active = TRUE
        AND u.username <> p_username;

      -- Reactivate the requested username
      UPDATE register.username u
      SET is_active = TRUE
      WHERE u.username = p_username
      RETURNING * INTO o_username;
  END;

  p_username := o_username.username;
END;
$$ LANGUAGE plpgsql;
