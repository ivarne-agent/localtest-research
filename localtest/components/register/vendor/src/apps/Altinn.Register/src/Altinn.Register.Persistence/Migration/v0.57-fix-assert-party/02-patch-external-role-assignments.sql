CREATE OR REPLACE FUNCTION register.patch_external_role_assignments(
  p_flags register.db_feature_flag[],
  p_now timestamptz,
  p_from_party uuid,
  p_source register.external_role_source,
  p_cmd_id uuid,
  p_present register.arg_role_assignment[],
  p_absent register.arg_role_assignment[],
  p_absent_by_type text[]
)
RETURNS TABLE (
  version_id bigint,
  "type" register.external_role_assignment_event_type,
  identifier register.identifier,
  to_party uuid
) AS $$
DECLARE
  v_cmd_id uuid;
BEGIN
  INSERT INTO register.external_role_assignment_command_history (cmd_id, "source", from_party)
  VALUES (p_cmd_id, p_source, p_from_party)
  ON CONFLICT (cmd_id, "source", from_party) DO NOTHING
  RETURNING cmd_id INTO v_cmd_id;

  -- If the command has already been processed, return its existing events.
  IF v_cmd_id IS NULL THEN
    RETURN QUERY
    SELECT
      e.id version_id,
      e."type",
      e.identifier,
      e.to_party
    FROM register.external_role_assignment_event e
    WHERE e.cmd_id = p_cmd_id
      AND e."source" = p_source
      AND e.from_party = p_from_party
    ORDER BY e.id;
    RETURN;
  END IF;

  WITH
    resolved_raw AS (
      SELECT DISTINCT
        a.identifier,
        register.resolve_external_role_referenced_party(p_flags, a.to_party) AS to_party
      FROM unnest(p_absent) a
    ),
    resolved AS (
      SELECT *
      FROM resolved_raw r
      WHERE r.to_party IS NOT NULL
    ),
    deleted AS (
      DELETE FROM register.external_role_assignment era
      WHERE
        era.from_party = p_from_party
        AND era."source" = p_source
        AND (
          (era.identifier, era.to_party) IN (SELECT r.identifier, r.to_party FROM resolved r)
          OR era.identifier = ANY(p_absent_by_type)
        )
      RETURNING
        era.to_party,
        era.identifier
    )
  INSERT INTO
    register.external_role_assignment_event (
      "type",
      cmd_id,
      "source",
      identifier,
      from_party,
      to_party
    )
  SELECT
    'removed'::register.external_role_assignment_event_type,
    p_cmd_id,
    p_source,
    d.identifier,
    p_from_party,
    d.to_party
  FROM
    deleted d;

  WITH
    resolved AS (
      SELECT DISTINCT
        a.identifier,
        register.assert_external_role_referenced_party(p_flags, p_now, a.to_party) AS to_party
      FROM unnest(p_present) a
    ),
    inserted AS (
      INSERT INTO
        register.external_role_assignment AS i ("source", identifier, from_party, to_party)
      SELECT
        p_source,
        a.identifier,
        p_from_party,
        a.to_party
      FROM resolved a
      ON CONFLICT DO NOTHING
      RETURNING
        i.to_party,
        i.identifier
    )
  INSERT INTO
    register.external_role_assignment_event (
      "type",
      cmd_id,
      "source",
      identifier,
      from_party,
      to_party
    )
  SELECT
    'added'::register.external_role_assignment_event_type,
    p_cmd_id,
    p_source,
    i.identifier,
    p_from_party,
    i.to_party
  FROM
    inserted i;

  -- Return the events
  RETURN QUERY
  SELECT
    e.id version_id,
    e."type",
    e.identifier,
    e.to_party
  FROM register.external_role_assignment_event e
  WHERE e.cmd_id = p_cmd_id
    AND e."source" = p_source
    AND e.from_party = p_from_party
  ORDER BY e.id;
END;
$$ LANGUAGE plpgsql;
