-- include: display-name,identifiers
-- filter: lookup(self-identified.email; transforms: replace-with.main-units; filters: none)

WITH uuids_by_self_identified_email AS (
    SELECT si_u."uuid", party.version_id
    FROM register.self_identified_user AS si_u
    INNER JOIN register.party AS party USING (uuid)
    WHERE si_u.email = ANY (@selfIdentifiedEmails)
),
top_level_uuids_untransformed AS (
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
uuids AS (
    SELECT
        "uuid",
        parent_uuid,
        sort_first,
        sort_second
    FROM main_unit_trans
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
