CREATE OR REPLACE FUNCTION register.upsert_system_user(
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
  INOUT p_system_user_type register.system_user."type"%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_system_user register.system_user%ROWTYPE;
BEGIN
  ASSERT p_party_type = 'system-user', 'Party must be of type "system-user"';

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
  INTO o_system_user
  FROM register."system_user" u
  WHERE u.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the system_user does not exist, insert it and return it
  IF NOT FOUND THEN
    INSERT INTO register."system_user" ("uuid", "type")
    VALUES (p_uuid, p_system_user_type)
    RETURNING * INTO o_system_user;

  ELSE
    -- Update the system_user
    UPDATE register."system_user" u
    SET "type" = p_system_user_type
    WHERE u.uuid = p_uuid
    RETURNING * INTO o_system_user;
  END IF;

  p_system_user_type := o_system_user."type";
END;
$$ LANGUAGE plpgsql;
