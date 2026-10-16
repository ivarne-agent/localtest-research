CREATE OR REPLACE FUNCTION register.upsert_user(
  IN p_uuid register.user.uuid%TYPE,
  INOUT p_user_ids bigint[],
  IN s_username boolean,
  INOUT p_username text
)
AS $$
DECLARE
  db_active_user register.user%ROWTYPE;
  p_active_user_id bigint := p_user_ids[1];
  p_inactive_user_ids bigint[] := p_user_ids[2:];
  p_other_uuid register.user.uuid%TYPE;
  p_loop_user_id bigint;
BEGIN
  ASSERT array_length(p_user_ids, 1) IS NOT NULL, 'User ID array must not be empty';

  -- Get existing active user by UUID if it exists
  SELECT u.*
  INTO db_active_user
  FROM register.user u
  WHERE u.uuid = p_uuid
    AND u.is_active = TRUE;

  -- If the user does not exist, insert it
  IF NOT FOUND THEN
    INSERT INTO register.user (uuid, user_id, username, is_active)
    VALUES (p_uuid, p_active_user_id, p_username, TRUE)
    RETURNING * INTO db_active_user;

  ELSE
    -- Validate that the user_id is not updated to a different value
    IF db_active_user.user_id <> p_active_user_id THEN
      RAISE EXCEPTION 'Cannot update immutable field "user_id" on party, existing: %, updated: %, party_uuid: %', db_active_user.user_id, p_active_user_id, db_active_user.uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'user'
            , COLUMN = 'user_id';
    END IF;

    -- Update the username if it has changed
    IF s_username AND db_active_user.username IS DISTINCT FROM p_username THEN
      UPDATE register.user
      SET username = p_username
      WHERE id = db_active_user.id
      RETURNING * INTO db_active_user;
    END IF;

    -- Update username parameter (output var)
    SELECT db_active_user.username
    INTO p_username;
  END IF;

  -- If we have historical user ids, insert them as inactive
  IF array_length(p_inactive_user_ids, 1) IS NOT NULL THEN
    -- note: p_inactive_user_ids should never be long, and as such we're fine with using slower loops here
    FOR p_loop_user_id IN SELECT unnest(p_inactive_user_ids) LOOP
      -- Check if the user_id already exists in the database
      SELECT u.uuid
      INTO p_other_uuid
      FROM register.user u
      WHERE u.user_id = p_loop_user_id;

      IF NOT FOUND THEN
        -- ID not in use, insert it as inactive
        INSERT INTO register.user (uuid, user_id, is_active)
        VALUES (p_uuid, p_loop_user_id, FALSE);

      ELSIF p_other_uuid <> p_uuid THEN
        -- ID in use, but not by this party. Raise an error.
        RAISE EXCEPTION 'User ID % is already in use by another party %', p_loop_user_id, p_other_uuid
          USING ERRCODE = '23505'
              , SCHEMA = 'register'
              , TABLE = 'user'
              , COLUMN = 'user_id';
      END IF;
    END LOOP;
  END IF;

  -- Update p_user_ids to reflect what's in the database
  SELECT array_agg(u.user_id ORDER BY u.is_active DESC, u.user_id DESC)
  INTO p_user_ids
  FROM register.user u
  WHERE u.uuid = p_uuid;
END;
$$ LANGUAGE plpgsql;
