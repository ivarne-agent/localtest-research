CREATE OR REPLACE FUNCTION register.resolve_external_role_referenced_party(
  p_flags register.db_feature_flag[],
  p_party_ref register.arg_role_party_ref
)
RETURNS uuid
AS $$
DECLARE
  o_party_uuid uuid;
BEGIN
  CASE
    WHEN p_party_ref.party_uuid IS NOT NULL THEN
      -- we trust that if the caller provides a party uuid, it is valid and exists.
      -- if it doesn't, the foreign key constraint will catch it.
      RETURN p_party_ref.party_uuid;

    WHEN p_party_ref.person_identifier IS NOT NULL THEN
      SELECT p."uuid"
      INTO o_party_uuid
      FROM register.party p
      WHERE p.person_identifier = p_party_ref.person_identifier;

      RETURN o_party_uuid;

    WHEN p_party_ref.organization_identifier IS NOT NULL THEN
      SELECT p."uuid"
      INTO o_party_uuid
      FROM register.party p
      WHERE p.organization_identifier = p_party_ref.organization_identifier;

      RETURN o_party_uuid;

    ELSE
      RAISE EXCEPTION 'Invalid party reference: must contain either party_uuid, person_identifier, or organization_identifier'
        USING ERRCODE = 'ZZ002'
            , DATATYPE = 'register.arg_role_party_ref';
  END CASE;
END;
$$ LANGUAGE plpgsql;
