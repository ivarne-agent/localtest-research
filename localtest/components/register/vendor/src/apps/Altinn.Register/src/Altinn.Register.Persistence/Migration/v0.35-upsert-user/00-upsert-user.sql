CREATE OR REPLACE FUNCTION register.upsert_user_record(
  IN p_uuid uuid,
  IN p_user_id bigint,
  IN s_username boolean,
  IN p_username text,
  IN p_is_active boolean
)
RETURNS TABLE (
  party_updated boolean
)
AS $$
DECLARE
  o_user register."user"%ROWTYPE;
  l_was_active boolean;
BEGIN
  -- Get existing user by user_id
  SELECT u.*
  INTO o_user
  FROM register."user" u
  WHERE u.user_id = p_user_id
  FOR NO KEY UPDATE;

  -- If the party does not exist, insert it
  IF NOT FOUND THEN
    -- Assign defaults to optional fields
    IF NOT s_username THEN
      p_username := NULL;
    END IF;

    IF NOT p_is_active THEN
      p_username := NULL; -- username must be null if user is not active
    END IF;

    -- Insert the new user
    INSERT INTO register."user" (uuid, user_id, username, is_active)
    VALUES (p_uuid, p_user_id, p_username, p_is_active)
    RETURNING * INTO o_user;

    l_was_active := FALSE;

  ELSE
    -- Store previous active status
    l_was_active := o_user.is_active;

    -- Validate that the update does not modify any immutable fields
    IF o_user.uuid <> p_uuid THEN
      RAISE EXCEPTION 'Cannot update immutable field "uuid" on user, existing: %, updated: %, user_id: %', o_user.uuid, p_uuid, p_user_id
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = '"user"'
            , COLUMN = 'uuid';
    END IF;

    p_is_active := p_is_active AND o_user.is_active; -- prevent reactivation

    UPDATE register.user u
    SET username = CASE WHEN NOT p_is_active THEN NULL
                        WHEN s_username THEN p_username
                        ELSE u.username
                   END,
        is_active = p_is_active
    WHERE u.id = o_user.id
    RETURNING * INTO o_user;
  END IF;

  -- If the user is active, trigger party update by running an update
  IF o_user.is_active OR l_was_active THEN
    UPDATE register.party p
    SET is_deleted = p.is_deleted
    WHERE p.uuid = o_user.uuid;

    party_updated := TRUE;
  ELSE
    party_updated := FALSE;
  END IF;

  RETURN NEXT;
END;
$$ LANGUAGE plpgsql;
