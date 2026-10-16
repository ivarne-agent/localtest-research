CREATE OR REPLACE FUNCTION register.upsert_party_org(
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
  INOUT p_unit_status register.organization.unit_status%TYPE,
  INOUT p_unit_type register.organization.unit_type%TYPE,
  INOUT p_telephone_number register.organization.telephone_number%TYPE,
  INOUT p_mobile_number register.organization.mobile_number%TYPE,
  INOUT p_fax_number register.organization.fax_number%TYPE,
  INOUT p_email_address register.organization.email_address%TYPE,
  INOUT p_internet_address register.organization.internet_address%TYPE,
  INOUT p_mailing_address register.organization.mailing_address%TYPE,
  INOUT p_business_address register.organization.business_address%TYPE,
  OUT o_version_id register.party.version_id%TYPE
)
AS $$
DECLARE
  o_org register.organization%ROWTYPE;
BEGIN
  ASSERT p_party_type = 'organization', 'Party must be of type "organization"';

  -- Upsert the party
  SELECT (register.upsert_party(
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
    p_is_deleted)).*
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
    o_version_id;

  -- Get existing organization by UUID if it exists
  SELECT o.*
  INTO o_org
  FROM register.organization o
  WHERE o.uuid = p_uuid
  FOR NO KEY UPDATE;

  -- If the organization does not exist, insert it and return it
  IF NOT FOUND THEN
    INSERT INTO register.organization ("uuid", unit_status, unit_type, telephone_number, mobile_number, fax_number, email_address, internet_address, mailing_address, business_address)
    VALUES (p_uuid, p_unit_status, p_unit_type, p_telephone_number, p_mobile_number, p_fax_number, p_email_address, p_internet_address, p_mailing_address, p_business_address)
    RETURNING * INTO o_org;

  ELSE
    -- Update the organization
    UPDATE register.organization o
    SET unit_status = p_unit_status
      , unit_type = p_unit_type
      , telephone_number = p_telephone_number
      , mobile_number = p_mobile_number
      , fax_number = p_fax_number
      , email_address = p_email_address
      , internet_address = p_internet_address
      , mailing_address = p_mailing_address
      , business_address = p_business_address
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
END;
$$ LANGUAGE plpgsql;
