-- include: party,person,org,si,sysuser,user
-- filter: lookup(id,uuid,external-urn,pers.id,org.id,user.id,user.name,self-identified.email; transforms: none; filters: none)

WITH uuids_by_party_uuid AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."uuid" = ANY (@partyUuids)
),
uuids_by_party_id AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."id" = ANY (@partyIds)
),
uuids_by_external_urn AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."ext_urn" = ANY (@externalUrns)
),
uuids_by_person_identifier AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."person_identifier" = ANY (@personIdentifiers)
),
uuids_by_organization_identifier AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."organization_identifier" = ANY (@organizationIdentifiers)
),
uuids_by_user_id AS (
    SELECT "user"."uuid", party.version_id
    FROM register."user" AS "user"
    INNER JOIN register.party AS party USING (uuid)
    WHERE "user".user_id = ANY (@userIds)
),
uuids_by_username AS (
    SELECT "username"."uuid", party.version_id
    FROM register."username" AS "username"
    INNER JOIN register.party AS party USING (uuid)
    WHERE "username".username = ANY (@usernames)
),
uuids_by_self_identified_email AS (
    SELECT si_u."uuid", party.version_id
    FROM register.self_identified_user AS si_u
    INNER JOIN register.party AS party USING (uuid)
    WHERE si_u.email = ANY (@selfIdentifiedEmails)
),
top_level_uuids_untransformed AS (
    SELECT "uuid", version_id FROM uuids_by_party_uuid
    UNION
    SELECT "uuid", version_id FROM uuids_by_party_id
    UNION
    SELECT "uuid", version_id FROM uuids_by_external_urn
    UNION
    SELECT "uuid", version_id FROM uuids_by_person_identifier
    UNION
    SELECT "uuid", version_id FROM uuids_by_organization_identifier
    UNION
    SELECT "uuid", version_id FROM uuids_by_user_id
    UNION
    SELECT "uuid", version_id FROM uuids_by_username
    UNION
    SELECT "uuid", version_id FROM uuids_by_self_identified_email
),
trans_input AS (
    SELECT
        "uuid" AS "uuid",
        NULL::uuid AS parent_uuid,
        version_id AS sort_first,
        NULL::uuid AS sort_second
    FROM top_level_uuids_untransformed
),
uuids AS (
    SELECT
        "uuid",
        parent_uuid,
        sort_first,
        sort_second
    FROM trans_input
),
filtered_user_ids AS (
    SELECT "user".*
    FROM register."user" AS "user"
    WHERE "user".is_active
       OR "user".user_id = ANY (@userIds)
),
filtered_usernames AS (
    SELECT "username".*
    FROM register."username" AS "username"
    WHERE "username".is_active
       OR "username".username = ANY (@usernames)
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
