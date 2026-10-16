
CREATE OR REPLACE FUNCTION register.upsert_party_pers(
  INOUT p_uuid register.party.uuid%TYPE,
  INOUT p_id register.party.id%TYPE,
  INOUT p_user_ids bigint[],
  IN s_username boolean,
  INOUT p_username text,
  INOUT p_party_type register.party.party_type%TYPE,
  INOUT p_display_name register.party.display_name%TYPE,
  INOUT p_person_identifier register.party.person_identifier%TYPE,
  INOUT p_organization_identifier register.party.organization_identifier%TYPE,
  INOUT p_created register.party.created%TYPE,
  INOUT p_updated register.party.updated%TYPE,
  INOUT p_is_deleted register.party.is_deleted%TYPE,
  IN s_owner boolean,
  INOUT p_owner register.party.owner%TYPE,
  INOUT p_first_name register.person.first_name%TYPE,
  INOUT p_middle_name register.person.middle_name%TYPE,
  INOUT p_last_name register.person.last_name%TYPE,
  INOUT p_short_name register.person.short_name%TYPE,
  INOUT p_date_of_birth register.person.date_of_birth%TYPE,
  INOUT p_date_of_death register.person.date_of_death%TYPE,
  INOUT p_address register.person.address%TYPE,
  INOUT p_mailing_address register.person.mailing_address%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_pers register.person%ROWTYPE;
BEGIN
  ASSERT p_party_type = 'person', 'Party must be of type "person"';

  -- Upsert the party
  SELECT *
  FROM register.upsert_party(
    p_uuid,
    p_id,
    p_user_ids,
    s_username,
    p_username,
    p_party_type,
    p_display_name,
    p_person_identifier,
    p_organization_identifier,
    p_created,
    p_updated,
    p_is_deleted,
    s_owner,
    p_owner)
  INTO
    p_uuid,
    p_id,
    p_user_ids,
    p_username,
    p_party_type,
    p_display_name,
    p_person_identifier,
    p_organization_identifier,
    p_created,
    p_updated,
    p_is_deleted,
    p_owner,
    o_version_id;

  -- Get existing person by UUID if it exists
  SELECT p.*
  INTO o_pers
  FROM register.person p
  WHERE p.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the person does not exist, insert it and return it
  IF NOT FOUND THEN
    INSERT INTO register.person ("uuid", first_name, middle_name, last_name, short_name, date_of_birth, date_of_death, address, mailing_address)
    VALUES (p_uuid, p_first_name, p_middle_name, p_last_name, p_short_name, p_date_of_birth, p_date_of_death, p_address, p_mailing_address)
    RETURNING * INTO o_pers;

  ELSE
    -- Update the person
    UPDATE register.person p
    SET first_name = p_first_name
      , middle_name = p_middle_name
      , last_name = p_last_name
      , short_name = p_short_name
      , date_of_birth = p_date_of_birth
      , date_of_death = p_date_of_death
      , address = p_address
      , mailing_address = p_mailing_address
    WHERE p.uuid = p_uuid
    RETURNING * INTO o_pers;
  END IF;

  p_first_name := o_pers.first_name;
  p_middle_name := o_pers.middle_name;
  p_last_name := o_pers.last_name;
  p_short_name := o_pers.short_name;
  p_date_of_birth := o_pers.date_of_birth;
  p_date_of_death := o_pers.date_of_death;
  p_address := o_pers.address;
  p_mailing_address := o_pers.mailing_address;
END;
$$ LANGUAGE plpgsql;
