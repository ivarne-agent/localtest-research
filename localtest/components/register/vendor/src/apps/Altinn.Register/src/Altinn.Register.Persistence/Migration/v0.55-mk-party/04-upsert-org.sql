CREATE OR REPLACE FUNCTION register.upsert_party_org(
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
  IN s_unit_status boolean,
  INOUT p_unit_status register.organization.unit_status%TYPE,
  IN s_unit_type boolean,
  INOUT p_unit_type register.organization.unit_type%TYPE,
  IN s_telephone_number boolean,
  INOUT p_telephone_number register.organization.telephone_number%TYPE,
  IN s_mobile_number boolean,
  INOUT p_mobile_number register.organization.mobile_number%TYPE,
  IN s_fax_number boolean,
  INOUT p_fax_number register.organization.fax_number%TYPE,
  IN s_email_address boolean,
  INOUT p_email_address register.organization.email_address%TYPE,
  IN s_internet_address boolean,
  INOUT p_internet_address register.organization.internet_address%TYPE,
  IN s_mailing_address boolean,
  INOUT p_mailing_address register.organization.mailing_address%TYPE,
  IN s_business_address boolean,
  INOUT p_business_address register.organization.business_address%TYPE,
  IN s_source boolean,
  INOUT p_source register.organization.source%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_org register.organization%ROWTYPE;
BEGIN
  ASSERT p_party_type = 'organization', 'Party must be of type "organization"';

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

  -- Get existing organization by UUID if it exists
  SELECT o.*
  INTO o_org
  FROM register.organization o
  WHERE o.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the organization does not exist, insert it and return it
  IF NOT FOUND THEN
    -- Assign defaults to optional fields
    IF NOT s_unit_status THEN
      p_unit_status := NULL;
    END IF;

    IF NOT s_unit_type THEN
      p_unit_type := NULL;
    END IF;

    IF NOT s_telephone_number THEN
      p_telephone_number := NULL;
    END IF;

    IF NOT s_mobile_number THEN
      p_mobile_number := NULL;
    END IF;

    IF NOT s_fax_number THEN
      p_fax_number := NULL;
    END IF;

    IF NOT s_email_address THEN
      p_email_address := NULL;
    END IF;

    IF NOT s_internet_address THEN
      p_internet_address := NULL;
    END IF;

    IF NOT s_mailing_address THEN
      p_mailing_address := NULL;
    END IF;

    IF NOT s_business_address THEN
      p_business_address := NULL;
    END IF;

    -- Validate required fields
    IF NOT s_source THEN
      RAISE EXCEPTION 'null value in column "source" of relation "organization" violates not-null constraint'
        USING ERRCODE = '23502'
            , SCHEMA = 'register'
            , TABLE = 'organization'
            , COLUMN = 'source';
    END IF;

    INSERT INTO register.organization ("uuid", unit_status, unit_type, telephone_number, mobile_number, fax_number, email_address, internet_address, mailing_address, business_address, source)
    VALUES (p_uuid, p_unit_status, p_unit_type, p_telephone_number, p_mobile_number, p_fax_number, p_email_address, p_internet_address, p_mailing_address, p_business_address, p_source)
    RETURNING * INTO o_org;

  ELSE
    -- Update the organization
    UPDATE register.organization o
    SET unit_status = CASE WHEN s_unit_status THEN p_unit_status ELSE o.unit_status END
      , unit_type = CASE WHEN s_unit_type THEN p_unit_type ELSE o.unit_type END
      , telephone_number = CASE WHEN s_telephone_number THEN p_telephone_number ELSE o.telephone_number END
      , mobile_number = CASE WHEN s_mobile_number THEN p_mobile_number ELSE o.mobile_number END
      , fax_number = CASE WHEN s_fax_number THEN p_fax_number ELSE o.fax_number END
      , email_address = CASE WHEN s_email_address THEN p_email_address ELSE o.email_address END
      , internet_address = CASE WHEN s_internet_address THEN p_internet_address ELSE o.internet_address END
      , mailing_address = CASE WHEN s_mailing_address THEN p_mailing_address ELSE o.mailing_address END
      , business_address = CASE WHEN s_business_address THEN p_business_address ELSE o.business_address END
      , source = CASE
          WHEN s_source THEN p_source
          ELSE o.source
        END
    WHERE o.uuid = p_uuid
    RETURNING * INTO o_org;
  END IF;

  p_unit_status := o_org.unit_status;
  p_unit_type := o_org.unit_type;
  p_telephone_number := o_org.telephone_number;
  p_mobile_number := o_org.mobile_number;
  p_fax_number := o_org.fax_number;
  p_email_address := o_org.email_address;
  p_internet_address := o_org.internet_address;
  p_mailing_address := o_org.mailing_address;
  p_business_address := o_org.business_address;
  p_source := o_org.source;
END;
$$ LANGUAGE plpgsql;
