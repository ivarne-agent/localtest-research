-- include: display-name,identifiers
-- filter: lookup(org.id; transforms: replace-with.main-units,include.sub-units; filters: type)

WITH uuids_by_organization_identifier AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    WHERE party."organization_identifier" = ANY (@organizationIdentifiers)
),
top_level_uuids_untransformed AS (
    SELECT "uuid", version_id FROM uuids_by_organization_identifier
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
)
SELECT
    uuids.parent_uuid p_parent_uuid,
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
