-- include: party,person,org,si,sysuser,user
-- filter: lookup(id; transforms: replace-with.main-units,include.sub-units; filters: type)

WITH uuids_by_party_id AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."id" = ANY (@partyIds)
),
top_level_uuids_untransformed AS (
    SELECT "uuid", version_id FROM uuids_by_party_id
),
trans_input AS (
    SELECT
        "uuid" AS "uuid",
        NULL::uuid AS parent_uuid,
        version_id AS sort_first,
        NULL::uuid AS sort_second
    FROM top_level_uuids_untransformed
),
main_unit_trans AS (
    SELECT DISTINCT
        party."uuid" AS "uuid",
        NULL::uuid AS parent_uuid,
        party.version_id AS sort_first,
        NULL::uuid AS sort_second
    FROM register.external_main_unit_role mur
    JOIN register.external_role_assignment ra ON mur.source = ra.source AND mur.identifier = ra.identifier
    JOIN register.party party ON ra.to_party = party."uuid"
    JOIN trans_input source ON ra.from_party = source."uuid"
),
sub_units AS (
    SELECT
        source."uuid" AS parent_uuid,
        source.sort_first AS sort_first,
        ra."from_party" AS child_uuid
    FROM main_unit_trans AS source
    JOIN register.external_role_assignment ra ON ra.to_party = source."uuid"
    JOIN register.external_main_unit_role mur ON mur.source = ra.source AND mur.identifier = ra.identifier
),
sub_units_trans AS (
    SELECT
        "uuid" AS "uuid",
        NULL::uuid AS parent_uuid,
        sort_first AS sort_first,
        NULL::uuid AS sort_second
    FROM main_unit_trans
    
    UNION ALL
    
    SELECT
        child_uuid AS "uuid",
        parent_uuid,
        sort_first,
        child_uuid AS sort_second
    FROM sub_units
),
top_level_uuids_unfiltered AS (
    SELECT
        "uuid",
        parent_uuid,
        sort_first,
        sort_second
    FROM sub_units_trans
),
uuids AS (
    SELECT party."uuid", party.version_id
    FROM top_level_uuids_unfiltered AS source
    INNER JOIN register.party AS party USING (uuid)
    WHERE party.party_type = ANY (@partyTypes)
),
filtered_user_ids AS (
    SELECT "user".*
    FROM register."user" AS "user"
    WHERE "user".is_active
),
filtered_usernames AS (
    SELECT "username".*
    FROM register."username" AS "username"
    WHERE "username".is_active
),
aggregated_user_ids AS (
    SELECT
        uuid,
        max(user_id) FILTER (WHERE is_active) as user_id,
        array_agg(user_id ORDER BY is_active DESC, user_id DESC) as user_ids
    FROM filtered_user_ids
    INNER JOIN uuids USING (uuid)
    GROUP BY uuid
),
aggregated_usernames AS (
    SELECT
        uuid,
        max(username) FILTER (WHERE is_active) as username,
        array_agg(username ORDER BY is_active DESC, username) as usernames
    FROM filtered_usernames
    INNER JOIN uuids USING (uuid)
    GROUP BY uuid
)
SELECT
    uuids.parent_uuid p_parent_uuid,
    party.uuid p_uuid,
    party.id p_id,
    party.party_type p_party_type,
    party.ext_urn p_ext_urn,
    party.display_name p_display_name,
    party.person_identifier p_person_identifier,
    party.organization_identifier p_organization_identifier,
    party.created p_created,
    party.updated p_updated,
    party.is_deleted p_is_deleted,
    party.deleted_at p_deleted_at,
    party.version_id p_version_id,
    party."owner" p_owner_uuid,
    person.first_name p_first_name,
    person.middle_name p_middle_name,
    person.last_name p_last_name,
    person.short_name p_short_name,
    person.date_of_birth p_date_of_birth,
    person.date_of_death p_date_of_death,
    person.address p_address,
    person.mailing_address p_person_mailing_address,
    person.source p_person_source,
    org.unit_status p_unit_status,
    org.unit_type p_unit_type,
    org.telephone_number p_telephone_number,
    org.mobile_number p_mobile_number,
    org.fax_number p_fax_number,
    org.email_address p_email_address,
    org.internet_address p_internet_address,
    org.mailing_address p_org_mailing_address,
    org.business_address p_business_address,
    org.source p_organization_source,
    si_u."type" p_self_identified_user_type,
    si_u.email p_self_identified_user_email,
    si_u.ext_ref p_self_identified_user_ext_ref,
    sys_u."type" p_system_user_type,
    agg_uid.user_id u_user_id,
    agg_uid.user_ids u_user_ids,
    agg_uname.username u_username,
    agg_uname.usernames u_usernames
FROM uuids AS uuids
INNER JOIN register.party AS party USING (uuid)
LEFT JOIN register.person AS person USING (uuid)
LEFT JOIN register.organization AS org USING (uuid)
LEFT JOIN register.self_identified_user AS si_u USING (uuid)
LEFT JOIN register.system_user AS sys_u USING (uuid)
LEFT JOIN aggregated_user_ids AS agg_uid USING (uuid)
LEFT JOIN aggregated_usernames AS agg_uname USING (uuid)
ORDER BY
    uuids.sort_first,
    uuids.sort_second NULLS FIRST
