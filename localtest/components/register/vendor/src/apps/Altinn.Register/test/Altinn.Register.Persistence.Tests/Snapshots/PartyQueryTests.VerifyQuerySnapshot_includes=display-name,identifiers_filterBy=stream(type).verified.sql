-- include: display-name,identifiers
-- filter: stream(type)

WITH maxval AS (
    SELECT register.tx_max_safeval('register.party_version_id_seq') maxval
),
top_level_uuids_untransformed AS (
    SELECT party."uuid", party.version_id
    FROM register.party AS party
    CROSS JOIN maxval mv
    WHERE party.version_id > @streamFromExclusive
      AND party.version_id <= mv.maxval
      AND party.party_type = ANY (@partyTypes)
    ORDER BY party.version_id ASC
    LIMIT @streamLimit
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
