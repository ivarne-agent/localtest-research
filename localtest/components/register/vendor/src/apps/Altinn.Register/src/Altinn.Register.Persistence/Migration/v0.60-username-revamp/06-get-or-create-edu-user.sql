CREATE OR REPLACE FUNCTION register.get_or_create_edu_user(
  IN p_flags register.db_feature_flag[],
  INOUT p_display_name register.party.display_name%TYPE,
  INOUT p_created register.party.created%TYPE,
  INOUT p_updated register.party.updated%TYPE,
  INOUT p_self_identified_ext_ref register.self_identified_user.ext_ref%TYPE,
  OUT p_uuid register.party.uuid%TYPE,
  OUT p_id register.party.id%TYPE,
  OUT p_ext_urn register.party.ext_urn%TYPE,
  OUT p_user_ids bigint[],
  OUT p_username text,
  OUT p_party_type register.party.party_type%TYPE,
  OUT p_person_identifier register.party.person_identifier%TYPE,
  OUT p_organization_identifier register.party.organization_identifier%TYPE,
  OUT p_is_deleted register.party.is_deleted%TYPE,
  OUT p_deleted_at register.party.deleted_at%TYPE,
  OUT p_owner register.party.owner%TYPE,
  OUT p_self_identified_user_type register.self_identified_user."type"%TYPE,
  OUT p_self_identified_email register.self_identified_user.email%TYPE,
  OUT o_version_id register.party.version_id%TYPE,
  OUT o_is_new boolean
)
AS $$
DECLARE
  o_result RECORD;
  o_party register.party%ROWTYPE;
  o_self_identified_user register.self_identified_user%ROWTYPE;
  o_user register."user"%ROWTYPE;
  o_username register.username%ROWTYPE;
BEGIN
  SELECT p, si, u, un
  INTO o_result
  FROM register.self_identified_user si
  JOIN register.party p USING ("uuid")
  JOIN register."user" u ON u.uuid = p.uuid
  LEFT JOIN register.username un ON un.uuid = p.uuid AND un.is_active = TRUE
  WHERE si.ext_ref = p_self_identified_ext_ref
  LIMIT 1;

  IF FOUND THEN
    o_party := o_result.p;
    o_self_identified_user := o_result.si;
    o_user := o_result.u;
    o_username := o_result.un;

  ELSE
    IF NOT 'create_party_id' = ANY(p_flags) THEN
      RAISE EXCEPTION 'Creating new party-ids is not allowed without "create_party_id" feature flag'
        USING ERRCODE = 'ZZ001'
            , SCHEMA = 'register'
            , TABLE = 'party';
    END IF;

    p_uuid := gen_random_uuid();
    p_id := nextval('register.party_inc_id_seq');

    SELECT *
      FROM register.upsert_self_identified_user(
        p_flags,
        TRUE, p_uuid,
        TRUE, p_id,
        p_ext_urn,
        ARRAY[p_id], -- user_ids
        FALSE, NULL, -- username
        'self-identified-user', -- party_type
        TRUE, p_display_name, -- display_name
        NULL, -- person_identifier
        NULL, -- organization_identifier
        p_created,
        p_updated,
        TRUE, FALSE, -- is_deleted
        TRUE, NULL, -- deleted_at
        TRUE, NULL, -- owner
        'edu', -- self_identified_user_type
        FALSE, NULL, -- self_identified_email
        TRUE, p_self_identified_ext_ref)
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
        p_self_identified_user_type,
        p_self_identified_email,
        p_self_identified_ext_ref,
        o_version_id;

    o_is_new := TRUE;
    RETURN;  -- success, return early
  END IF; -- if not found

  p_uuid := o_party.uuid;
  p_id := o_party.id;
  p_ext_urn := o_party.ext_urn;
  p_user_ids := ARRAY[o_user.user_id];
  p_username := o_username.username;
  p_party_type := o_party.party_type;
  p_display_name := o_party.display_name;
  p_person_identifier := o_party.person_identifier;
  p_organization_identifier := o_party.organization_identifier;
  p_created := o_party.created;
  p_updated := o_party.updated;
  p_is_deleted := o_party.is_deleted;
  p_deleted_at := o_party.deleted_at;
  p_owner := o_party.owner;
  p_self_identified_user_type := o_self_identified_user."type";
  p_self_identified_email := o_self_identified_user.email;
  p_self_identified_ext_ref := o_self_identified_user.ext_ref;
  o_version_id := o_party.version_id;
  o_is_new := FALSE;
END;
$$ LANGUAGE plpgsql;
