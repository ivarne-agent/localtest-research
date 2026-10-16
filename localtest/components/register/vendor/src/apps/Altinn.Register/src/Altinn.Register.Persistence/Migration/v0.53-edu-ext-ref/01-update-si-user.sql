CREATE OR REPLACE FUNCTION register.upsert_self_identified_user(
  INOUT p_uuid register.party.uuid%TYPE,
  INOUT p_id register.party.id%TYPE,
  INOUT p_ext_urn register.party.ext_urn%TYPE,
  INOUT p_user_ids bigint[],
  IN s_username boolean,
  INOUT p_username text,
  INOUT p_party_type register.party.party_type%TYPE,
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
  INOUT p_self_identified_user_type register.self_identified_user."type"%TYPE,
  IN s_self_identified_email boolean,
  INOUT p_self_identified_email register.self_identified_user.email%TYPE,
  IN s_self_identified_ext_ref boolean,
  INOUT p_self_identified_ext_ref register.self_identified_user.ext_ref%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_self_identified_user register.self_identified_user%ROWTYPE;
BEGIN
  ASSERT p_party_type = 'self-identified-user', 'Party must be of type "self-identified-user"';
  CASE
    WHEN p_self_identified_user_type = 'legacy' THEN
      ASSERT starts_with(p_ext_urn, 'urn:altinn:person:legacy-selfidentified:'), 'Invalid ext_urn format for legacy self-identified user';
    WHEN p_self_identified_user_type = 'edu' THEN
      ASSERT p_ext_urn IS NULL, 'ext_urn must be NULL for edu self-identified user';
    WHEN p_self_identified_user_type = 'idporten-email' THEN
      ASSERT starts_with(p_ext_urn, 'urn:altinn:person:idporten-email:'), 'Invalid ext_urn format for idporten-email self-identified user';
    ELSE
      RAISE EXCEPTION 'Unknown self-identified user type: %', p_self_identified_user_type
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'self_identified_user'
            , COLUMN = 'type';
  END CASE;

  -- Upsert the party
  SELECT *
  FROM register.upsert_party(
    p_uuid,
    p_id,
    p_ext_urn,
    p_user_ids,
    s_username,
    p_username,
    p_party_type,
    p_display_name,
    p_person_identifier,
    p_organization_identifier,
    p_created,
    p_updated,
    s_is_deleted,
    p_is_deleted,
    s_deleted_at,
    p_deleted_at,
    s_owner,
    p_owner)
  INTO
    p_uuid,
    p_id,
    p_ext_urn,
    p_user_ids,
    p_username,
    p_party_type,
    p_display_name,
    p_person_identifier,
    p_organization_identifier,
    p_created,
    p_updated,
    p_is_deleted,
    p_deleted_at,
    p_owner,
    o_version_id;

  -- Get existing system_user by UUID if it exists
  SELECT u.*
  INTO o_self_identified_user
  FROM register."self_identified_user" u
  WHERE u.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the system_user does not exist, insert it and return it
  IF NOT FOUND THEN
    -- Assign defaults to optional fields
    IF NOT s_self_identified_email THEN
      p_self_identified_email := NULL;
    END IF;

    IF NOT s_self_identified_ext_ref THEN
      p_self_identified_ext_ref := NULL;
    END IF;

    INSERT INTO register."self_identified_user" ("uuid", "type", "email", "ext_ref")
    VALUES (p_uuid, p_self_identified_user_type, p_self_identified_email, p_self_identified_ext_ref)
    RETURNING * INTO o_self_identified_user;

  ELSE
    -- Validate that the updated party does not modify any immutable fields
    IF o_self_identified_user."type" <> p_self_identified_user_type THEN
      RAISE EXCEPTION 'Cannot update immutable field "type" on self_identified_user, existing: %, updated: %, party_uuid: %', o_self_identified_user."type", p_self_identified_user_type, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'self_identified_user'
            , COLUMN = 'type';
    END IF;

    IF s_self_identified_email AND o_self_identified_user."email" IS DISTINCT FROM p_self_identified_email THEN
      RAISE EXCEPTION 'Cannot update immutable field "email" on self_identified_user, existing: %, updated: %, party_uuid: %', o_self_identified_user."email", p_self_identified_email, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'self_identified_user'
            , COLUMN = 'email';
    END IF;

    IF s_self_identified_ext_ref
      AND o_self_identified_user.ext_ref IS NOT NULL
      AND o_self_identified_user.ext_ref IS DISTINCT FROM p_self_identified_ext_ref
    THEN
      RAISE EXCEPTION 'Cannot update immutable field "ext_ref" on self_identified_user, existing: %, updated: %, party_uuid: %', o_self_identified_user.ext_ref, p_self_identified_ext_ref, p_uuid
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'self_identified_user'
            , COLUMN = 'ext_ref';
    END IF;

    UPDATE register.self_identified_user
    SET ext_ref = CASE WHEN s_self_identified_ext_ref THEN COALESCE(o_self_identified_user.ext_ref, p_self_identified_ext_ref) ELSE o_self_identified_user.ext_ref END
    WHERE uuid = p_uuid
    RETURNING * INTO o_self_identified_user;
  END IF;

  p_self_identified_user_type := o_self_identified_user."type";
  p_self_identified_email := o_self_identified_user."email";
  p_self_identified_ext_ref := o_self_identified_user.ext_ref;
END;
$$ LANGUAGE plpgsql;
