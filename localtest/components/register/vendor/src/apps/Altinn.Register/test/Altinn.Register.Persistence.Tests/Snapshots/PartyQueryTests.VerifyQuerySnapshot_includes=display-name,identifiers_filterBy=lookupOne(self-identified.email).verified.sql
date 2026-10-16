-- include: display-name,identifiers
-- filter: lookupOne(self-identified.email)

WITH top_level_uuids_untransformed AS (
    SELECT si_u."uuid", party.version_id
    FROM register.self_identified_user AS si_u
    INNER JOIN register.party AS party USING (uuid)
    WHERE si_u.email = @selfIdentifiedEmail
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
