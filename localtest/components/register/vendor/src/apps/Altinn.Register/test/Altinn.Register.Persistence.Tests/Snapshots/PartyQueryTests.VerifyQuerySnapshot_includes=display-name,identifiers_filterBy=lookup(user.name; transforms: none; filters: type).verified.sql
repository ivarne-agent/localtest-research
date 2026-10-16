-- include: display-name,identifiers
-- filter: lookup(user.name; transforms: none; filters: type)

WITH uuids_by_username AS (
    SELECT "username"."uuid", party.version_id
    FROM register."username" AS "username"
    INNER JOIN register.party AS party USING (uuid)
    WHERE "username".username = ANY (@usernames)
),
top_level_uuids_untransformed AS (
    SELECT "uuid", version_id FROM uuids_by_username
),
trans_input AS (
    SELECT
        "uuid" AS "uuid",
        NULL::uuid AS parent_uuid,
        version_id AS sort_first,
        NULL::uuid AS sort_second
    FROM top_level_uuids_untransformed
),
top_level_uuids_unfiltered AS (
    SELECT
        "uuid",
        parent_uuid,
        sort_first,
        sort_second
    FROM trans_input
),
uuids AS (
    SELECT party."uuid", party.version_id
    FROM top_level_uuids_unfiltered AS source
    INNER JOIN register.party AS party USING (uuid)
    WHERE party.party_type = ANY (@partyTypes)
)
SELECT
    party.uuid p_uuid,
    party.id p_id,
    party.party_type p_party_type,
    party.ext_urn p_ext_urn,
    party.display_name p_display_name,
    party.person_identifier p_person_identifier,
    party.organization_identifier p_organization_identifier
FROM uuids AS uuids
INNER JOIN register.party AS party USING (uuid)
ORDER BY
    uuids.sort_first,
    uuids.sort_second NULLS FIRST
