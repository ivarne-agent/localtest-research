CREATE OR REPLACE FUNCTION register.upsert_party_pers(
  IN p_flags register.db_feature_flag[],
  IN s_uuid boolean,
  INOUT p_uuid register.party.uuid%TYPE,
  IN s_id boolean,
  INOUT p_id register.party.id%TYPE,
  INOUT p_ext_urn register.party.ext_urn%TYPE,
  INOUT p_user_ids bigint[],
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
  IN s_first_name boolean,
  INOUT p_first_name register.person.first_name%TYPE,
  IN s_middle_name boolean,
  INOUT p_middle_name register.person.middle_name%TYPE,
  IN s_last_name boolean,
  INOUT p_last_name register.person.last_name%TYPE,
  IN s_short_name boolean,
  INOUT p_short_name register.person.short_name%TYPE,
  IN s_date_of_birth boolean,
  INOUT p_date_of_birth register.person.date_of_birth%TYPE,
  IN s_date_of_death boolean,
  INOUT p_date_of_death register.person.date_of_death%TYPE,
  IN s_address boolean,
  INOUT p_address register.person.address%TYPE,
  IN s_mailing_address boolean,
  INOUT p_mailing_address register.person.mailing_address%TYPE,
  IN s_source boolean,
  INOUT p_source register.person.source%TYPE,
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
    p_flags,
    s_uuid,
    p_uuid,
    s_id,
    p_id,
    p_ext_urn,
    p_user_ids,
    s_username, p_username,
    p_party_type,
    s_display_name,
    p_display_name,
    p_person_identifier,
    p_organization_identifier,
    p_created,
    p_updated,
    s_is_deleted, p_is_deleted,
    s_deleted_at, p_deleted_at,
    s_owner, p_owner)
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

  -- Get existing person by UUID if it exists
  SELECT p.*
  INTO o_pers
  FROM register.person p
  WHERE p.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the person does not exist, insert it and return it
  IF NOT FOUND THEN
    -- Assign defaults to optional fields
    IF NOT s_first_name THEN
      p_first_name := NULL;
    END IF;

    IF NOT s_middle_name THEN
      p_middle_name := NULL;
    END IF;

    IF NOT s_last_name THEN
      p_last_name := NULL;
    END IF;

    IF NOT s_short_name THEN
      p_short_name := NULL;
    END IF;

    IF NOT s_date_of_birth THEN
      p_date_of_birth := NULL;
    END IF;

    IF NOT s_date_of_death THEN
      p_date_of_death := NULL;
    END IF;

    IF NOT s_address THEN
      p_address := NULL;
    END IF;

    IF NOT s_mailing_address THEN
      p_mailing_address := NULL;
    END IF;

    -- Validate required fields
    IF NOT s_source THEN
      RAISE EXCEPTION 'null value in column "source" of relation "person" violates not-null constraint'
        USING ERRCODE = '23502'
            , SCHEMA = 'register'
            , TABLE = 'person'
            , COLUMN = 'source';
    END IF;

    INSERT INTO register.person ("uuid", first_name, middle_name, last_name, short_name, date_of_birth, date_of_death, address, mailing_address, source)
    VALUES (p_uuid, p_first_name, p_middle_name, p_last_name, p_short_name, p_date_of_birth, p_date_of_death, p_address, p_mailing_address, p_source)
    RETURNING * INTO o_pers;

  ELSE
    -- Update the person
    UPDATE register.person p
    SET first_name = CASE WHEN s_first_name THEN p_first_name ELSE p.first_name END
      , middle_name = CASE WHEN s_middle_name THEN p_middle_name ELSE p.middle_name END
      , last_name = CASE WHEN s_last_name THEN p_last_name ELSE p.last_name END
      , short_name = CASE WHEN s_short_name THEN p_short_name ELSE p.short_name END
      , date_of_birth = CASE WHEN s_date_of_birth THEN p_date_of_birth ELSE p.date_of_birth END
      , date_of_death = CASE WHEN s_date_of_death THEN p_date_of_death ELSE p.date_of_death END
      , address = CASE WHEN s_address THEN p_address ELSE p.address END
      , mailing_address = CASE WHEN s_mailing_address THEN p_mailing_address ELSE p.mailing_address END
      , source = CASE
          WHEN s_source THEN p_source
          ELSE p.source
        END
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
  p_source := o_pers.source;
END;
$$ LANGUAGE plpgsql;
