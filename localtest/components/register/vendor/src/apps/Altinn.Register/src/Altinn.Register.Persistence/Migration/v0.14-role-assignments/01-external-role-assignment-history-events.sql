CREATE TYPE register.external_role_assignment_event_type AS ENUM(
  'added',
  'removed'
);

CREATE TABLE register.external_role_assignment_command_history (
  cmd_id uuid NOT NULL, -- Command id of mass-transit command
  "source" register.external_role_source NOT NULL,
  from_party uuid NOT NULL,
  PRIMARY KEY (cmd_id, "source", from_party),
  CONSTRAINT external_role_assignment_command_history_from_party_fkey FOREIGN KEY (from_party) REFERENCES register.party(uuid) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE SEQUENCE register.external_role_assignment_event_id_seq AS bigint;

CREATE TABLE register.external_role_assignment_event (
  id bigint NOT NULL PRIMARY KEY DEFAULT register.tx_nextval('register.external_role_assignment_event_id_seq'),
  "type" register.external_role_assignment_event_type NOT NULL,
  cmd_id uuid NOT NULL, -- The id of the command that created the event
  "source" register.external_role_source NOT NULL,
	identifier register.identifier NOT NULL,
  from_party uuid NOT NULL,
	to_party uuid NOT NULL,
  CONSTRAINT external_role_assignment_event_from_party_fkey FOREIGN KEY (from_party) REFERENCES register.party(uuid) ON DELETE RESTRICT ON UPDATE CASCADE,
	CONSTRAINT external_role_assignment_event_to_party_fkey FOREIGN KEY (to_party) REFERENCES register.party(uuid) ON DELETE RESTRICT ON UPDATE CASCADE,
	CONSTRAINT external_role_assignment_event_source_identifier_fkey FOREIGN KEY ("source",identifier) REFERENCES register.external_role_definition("source",identifier) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT external_role_assignment_event_cmd_id_fkey FOREIGN KEY (cmd_id,"source",from_party) REFERENCES register.external_role_assignment_command_history(cmd_id,"source",from_party) ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE INDEX external_role_assignment_event_cmd_id_idx ON register.external_role_assignment_event USING hash (cmd_id);

-- Argument to upsert_external_role_assignmens
CREATE TYPE register.arg_upsert_external_role_assignment AS (
  to_party uuid,
  identifier register.identifier
);

CREATE FUNCTION register.upsert_external_role_assignments(
  p_from_party uuid,
  p_source register.external_role_source,
  p_cmd_id uuid,
  p_assignments register.arg_upsert_external_role_assignment[]
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
  -- Check if the command has already been processed
  SELECT h.cmd_id INTO v_cmd_id
  FROM register.external_role_assignment_command_history h
  WHERE h.cmd_id = p_cmd_id
    AND h."source" = p_source
    AND h.from_party = p_from_party;

  -- If the command has already been processed,
  -- simply return all the events associated with it
  IF v_cmd_id IS NOT NULL THEN
    RETURN QUERY
    SELECT
      e.id version_id,
      e."type",
      e.identifier,
      e.to_party
    FROM register.external_role_assignment_event e
    WHERE e.cmd_id = p_cmd_id
      AND e."source" = p_source
      AND e.from_party = p_from_party;
    RETURN;
  END IF;

  -- Insert the command id into the command history
  INSERT INTO register.external_role_assignment_command_history (cmd_id, "source", from_party)
  VALUES (p_cmd_id, p_source, p_from_party);

  -- Merge into register.external_role_assignment, and insert the events
  WITH
    deleted AS (
      DELETE FROM register.external_role_assignment era
      WHERE
        era.from_party = p_from_party
        AND era."source" = p_source
        AND NOT EXISTS (
          SELECT
            1
          FROM
            unnest(p_assignments) a
          WHERE
            a.to_party = era.to_party
            AND a.identifier = era.identifier
        )
      RETURNING
        era.to_party,
        era.identifier
    ),
    inserted AS (
      INSERT INTO
        register.external_role_assignment AS i ("source", identifier, from_party, to_party)
      SELECT
        p_source,
        a.identifier,
        p_from_party,
        a.to_party
      FROM
        unnest(p_assignments) a
      WHERE
        NOT EXISTS (
          SELECT
            1
          FROM
            register.external_role_assignment era
          WHERE
            era.from_party = p_from_party
            AND era."source" = p_source
            AND era.to_party = a.to_party
            AND era.identifier = a.identifier
        )
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
    'removed'::register.external_role_assignment_event_type,
    p_cmd_id,
    p_source,
    d.identifier,
    p_from_party,
    d.to_party
  FROM
    deleted d
  UNION
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
    AND e.from_party = p_from_party;
END;
$$ LANGUAGE plpgsql;
