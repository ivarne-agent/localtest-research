CREATE OR REPLACE FUNCTION register.upsert_party(
  IN p_flags register.db_feature_flag[],
  IN s_uuid boolean,
  INOUT p_uuid register.party.uuid%TYPE,
  IN s_id boolean,
  INOUT p_id register.party.id%TYPE,
  INOUT p_ext_urn register.party.ext_urn%TYPE,
  INOUT p_user_ids bigint[], -- The user_ids is considered unset if it is NULL
  IN s_username boolean,
  INOUT p_username text,
  INOUT p_party_type register.party.party_type%TYPE,
  IN s_display_name boolean,
  INOUT p_display_name register.party.display_name%TYPE,
  INOUT p_person_identifier register.party.person_identifier%TYPE,
  INOUT p_organization_identifier register.party.organization_identifier%TYPE,
  INOUT p_created register.party.created%TYPE,
  INOUT p_updated register.party.updated%TYPE,
  IN s_is_deleted boolean,
  INOUT p_is_deleted register.party.is_deleted%TYPE,
  IN s_deleted_at boolean,
  INOUT p_deleted_at register.party.deleted_at%TYPE,
  IN s_owner boolean,
  INOUT p_owner register.party.owner%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_party register.party%ROWTYPE;
BEGIN
  -- Validate some parameters
  IF s_uuid <> s_id THEN
    RAISE EXCEPTION 'Parameters "s_uuid" and "s_id" must be the same. Either both are known or none of them are.'
      USING ERRCODE = 'ZZ001'
          , SCHEMA = 'register'
          , TABLE = 'party';
  END IF;

  IF NOT s_uuid AND p_user_ids IS NOT NULL THEN
    RAISE EXCEPTION 'Parameter "p_user_ids" cannot be set without "s_uuid"'
      USING ERRCODE = 'ZZ001'
          , SCHEMA = 'register'
          , TABLE = 'party';
  END IF;

  -- Get existing party by UUID if it exists
  SELECT p.*
  INTO o_party
  FROM register.party p
  WHERE (s_uuid AND p.uuid = p_uuid)
     OR (NOT s_uuid AND p_person_identifier IS NOT NULL AND p.person_identifier = p_person_identifier)
     OR (NOT s_uuid AND p_organization_identifier IS NOT NULL AND p.organization_identifier = p_organization_identifier)
  LIMIT 1
  FOR NO KEY UPDATE;

  -- If the party does not exist, insert it and return it
  IF NOT FOUND THEN
    -- If the uuid/id is not provided, find the existing party by alternate keys (person_identifier or organization_identifier), or generate new ones
    IF NOT s_uuid THEN
      -- We've already attempted to find the party by alternate keys, so this means it's a new party that doesn't have a uuid/id yet.
      IF NOT 'create_party_id' = ANY(p_flags) THEN
        RAISE EXCEPTION 'Creating new party-ids is not allowed without "create_party_id" feature flag'
          USING ERRCODE = 'ZZ001'
              , SCHEMA = 'register'
              , TABLE = 'party';
      END IF;

      p_uuid := gen_random_uuid();
      p_id := nextval('register.party_inc_id_seq');

      IF p_party_type IN('person', 'self-identified-user') THEN
        p_user_ids := ARRAY[p_id];
      END IF;
    END IF;

    -- Assign defaults to optional fields
    IF NOT s_is_deleted THEN
      p_is_deleted := FALSE;
    END IF;

    IF NOT s_deleted_at THEN
      p_deleted_at := NULL;
    END IF;

    IF NOT s_owner THEN
      p_owner := NULL;
    END IF;

    -- Validate required fields
    IF NOT s_display_name THEN
      RAISE EXCEPTION 'null value in column "display_name" of relation "party" violates not-null constraint'
        USING ERRCODE = '23502'
            , SCHEMA = 'register'
            , TABLE = 'party'
            , COLUMN = 'display_name';
    END IF;

    -- Insert new party
    INSERT INTO register.party (uuid, id, party_type, ext_urn, display_name, person_identifier, organization_identifier, created, updated, is_deleted, deleted_at, "owner")
    VALUES (p_uuid, p_id, p_party_type, p_ext_urn, p_display_name, p_person_identifier, p_organization_identifier, p_created, p_updated, p_is_deleted, p_deleted_at, p_owner)
    RETURNING * INTO o_party;

  ELSE
    -- Validate that the updated party does not modify any immutable fields
    IF s_id AND o_party.id <> p_id THEN
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

    IF s_owner THEN
      IF o_party.owner <> p_owner THEN
        RAISE EXCEPTION 'Cannot update immutable field "owner" on party, existing: %, updated: %, party_uuid: %', o_party.owner, p_owner, p_uuid
          USING ERRCODE = 'ZZ001'
              , SCHEMA = 'register'
              , TABLE = 'party'
              , COLUMN = 'owner';
      END IF;
    END IF;

    -- Set expected fields based on lookup now allowing alternate keys
    p_uuid := o_party.uuid;
    p_id := o_party.id;

    UPDATE register.party p
    SET display_name = CASE WHEN s_display_name THEN p_display_name ELSE p.display_name END
      , updated = p_updated
      , is_deleted = CASE WHEN s_is_deleted THEN p_is_deleted ELSE p.is_deleted END
      , deleted_at = CASE WHEN s_deleted_at THEN p_deleted_at ELSE p.deleted_at END
      , ext_urn = p_ext_urn
    WHERE p.uuid = p_uuid
    RETURNING * INTO o_party;
  END IF;

  p_uuid := o_party.uuid;
  p_id := o_party.id;
  p_ext_urn := o_party.ext_urn;
  p_party_type := o_party.party_type;
  p_display_name := o_party.display_name;
  p_person_identifier := o_party.person_identifier;
  p_organization_identifier := o_party.organization_identifier;
  p_created := o_party.created;
  p_updated := o_party.updated;
  p_is_deleted := o_party.is_deleted;
  p_deleted_at := o_party.deleted_at;
  p_owner := o_party.owner;
  o_version_id := o_party.version_id;

  -- Upsert user-info
  IF p_user_ids IS NOT NULL THEN
    SELECT *
    FROM register.upsert_user(p_uuid, p_is_deleted, p_user_ids)
    INTO p_user_ids;
  END IF;

  IF s_username THEN
    SELECT p.p_username
    FROM register.set_username(p_uuid, p_username) p
    INTO p_username;
  ELSE
    -- If we're not updating the username, we need to ensure that the output parameter is set to the current value in case the caller relies on it
    SELECT u.username
    FROM register.username u
    WHERE u.uuid = p_uuid
      AND u.is_active = TRUE
    INTO p_username;
  END IF;
END;
$$ LANGUAGE plpgsql;
