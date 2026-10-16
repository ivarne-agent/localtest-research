CREATE OR REPLACE FUNCTION register.upsert_user(
  IN p_uuid register.user.uuid%TYPE,
  INOUT p_user_ids bigint[]
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
  WHERE u.uuid = p_uuid;

  -- If the user does not exist, insert it
  IF NOT FOUND THEN
    INSERT INTO register.user (uuid, user_id, is_active)
    VALUES (p_uuid, p_active_user_id, TRUE)
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

CREATE OR REPLACE FUNCTION register.upsert_party(
  INOUT p_uuid register.party.uuid%TYPE,
  INOUT p_id register.party.id%TYPE,
  INOUT p_user_ids bigint[],
  INOUT p_party_type register.party.party_type%TYPE,
  INOUT p_display_name register.party.display_name%TYPE,
  INOUT p_person_identifier register.party.person_identifier%TYPE,
  INOUT p_organization_identifier register.party.organization_identifier%TYPE,
  INOUT p_created register.party.created%TYPE,
  INOUT p_updated register.party.updated%TYPE,
  INOUT p_is_deleted register.party.is_deleted%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_party register.party%ROWTYPE;
BEGIN
  -- Get existing party by UUID if it exists
  SELECT p.*
  INTO o_party
  FROM register.party p
  WHERE p.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the party does not exist, insert it and return it
  IF NOT FOUND THEN
    INSERT INTO register.party (uuid, id, party_type, display_name, person_identifier, organization_identifier, created, updated, is_deleted)
    VALUES (p_uuid, p_id, p_party_type, p_display_name, p_person_identifier, p_organization_identifier, p_created, p_updated, p_is_deleted)
    RETURNING * INTO o_party;

  ELSE
    -- Validate that the updated party does not modify any immutable fields
    IF o_party.id <> p_id THEN
      RAISE EXCEPTION 'Cannot update immutable field "id" on party, existing: %, updated: %, party_uuid: %', o_party.id, p_id, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'party'
            , COLUMN = 'id';
    END IF;

    IF o_party.party_type <> p_party_type THEN
      RAISE EXCEPTION 'Cannot update immutable field "party_type" on party, existing: %, updated: %, party_uuid: %', o_party.party_type, p_party_type, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'party'
            , COLUMN = 'party_type';
    END IF;

    IF o_party.person_identifier <> p_person_identifier THEN
      RAISE EXCEPTION 'Cannot update immutable field "person_identifier" on party, existing: %, updated: %, party_uuid: %', o_party.person_identifier, p_person_identifier, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'party'
            , COLUMN = 'person_identifier';
    END IF;

    IF o_party.organization_identifier <> p_organization_identifier THEN
      RAISE EXCEPTION 'Cannot update immutable field "organization_identifier" on party, existing: %, updated: %, party_uuid: %', o_party.organization_identifier, p_organization_identifier, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'party'
            , COLUMN = 'organization_identifier';
    END IF;

    UPDATE register.party p
    SET display_name = p_display_name
      , updated = p_updated
      , is_deleted = p_is_deleted
    WHERE p.uuid = p_uuid
    RETURNING * INTO o_party;
  END IF;

  -- Upsert the user
  IF p_user_ids IS NOT NULL THEN
    SELECT register.upsert_user(p_uuid, p_user_ids)
    INTO p_user_ids;
  END IF;

  p_uuid := o_party.uuid;
  p_id := o_party.id;
  p_party_type := o_party.party_type;
  p_display_name := o_party.display_name;
  p_person_identifier := o_party.person_identifier;
  p_organization_identifier := o_party.organization_identifier;
  p_created := o_party.created;
  p_updated := o_party.updated;
  p_is_deleted := o_party.is_deleted;
  o_version_id := o_party.version_id;
END;
$$ LANGUAGE plpgsql;
