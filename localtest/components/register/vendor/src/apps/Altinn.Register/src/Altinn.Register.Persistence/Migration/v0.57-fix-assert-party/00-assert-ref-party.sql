CREATE OR REPLACE FUNCTION register.assert_external_role_referenced_party(
  p_flags register.db_feature_flag[],
  p_now timestamptz,
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

      IF NOT FOUND THEN
        IF NOT 'create_party_id' = ANY(p_flags) THEN
          RAISE EXCEPTION 'No party found with person identifier %', p_party_ref.person_identifier
            USING ERRCODE = 'ZZ003'
                , DATATYPE = 'register.arg_role_party_ref';
        END IF;

        -- If the person doesn't exist, we create it
        SELECT p_uuid
        INTO o_party_uuid
        FROM register.upsert_party_pers(
          p_flags,
          false, NULL, -- uuid
          false, NULL, -- id
          'urn:altinn:person:identifier-no:' || p_party_ref.person_identifier, -- ext_urn
          NULL, -- user_ids
          false, NULL, -- username
          'person', -- party_type
          true, (p_party_ref.person_name).display_name, -- display_name
          p_party_ref.person_identifier, -- person_identifier
          NULL, -- organization_identifier
          p_now, -- created
          p_now, -- updated
          true, false, -- is_deleted
          true, NULL, -- deleted_at
          true, NULL, -- owner
          true, (p_party_ref.person_name).first_name, -- first_name
          true, (p_party_ref.person_name).middle_name, -- middle_name
          true, (p_party_ref.person_name).last_name, -- last_name
          true, (p_party_ref.person_name).short_name, -- short_name
          true, NULL, -- date_of_birth
          true, NULL, -- date_of_death
          true, NULL, -- address
          true, p_party_ref.mailing_address, -- mailing_address
          true, 'npr'); -- source - this is technically not true, but the person does belong to NPR
      END IF;

      RETURN o_party_uuid;

    WHEN p_party_ref.organization_identifier IS NOT NULL THEN
      SELECT p."uuid"
      INTO o_party_uuid
      FROM register.party p
      WHERE p.organization_identifier = p_party_ref.organization_identifier;

      IF NOT FOUND THEN
        -- we do not create organizations implicitly, as this should be handled by re-ordering the queue and retries
        RAISE EXCEPTION 'No party found with organization identifier %', p_party_ref.organization_identifier
          USING ERRCODE = 'ZZ003'
              , DATATYPE = 'register.arg_role_party_ref';
      END IF;

      RETURN o_party_uuid;

    ELSE
      RAISE EXCEPTION 'Invalid party reference: must contain either party_uuid, person_identifier, or organization_identifier'
        USING ERRCODE = 'ZZ002'
            , DATATYPE = 'register.arg_role_party_ref';
  END CASE;
END;
$$ LANGUAGE plpgsql;
