-- Returns:
-- | 0:Success { policy: text, resource: text, subject: text, count: integer, window_started_at: timestamp with time zone, window_expires_at: timestamp with time zone, blocked_until: timestamp with time zone, retain_until: timestamp with time zone, created: timestamp with time zone, updated: timestamp with time zone, is_blocked: boolean }
CREATE OR REPLACE FUNCTION register.rate_limit_record_leading_edge(
  p_policy text,
  p_resource text,
  p_subject text,
  p_now timestamp with time zone,
  p_cost integer,
  p_limit integer,
  p_window_duration interval,
  p_block_duration interval
)
RETURNS TABLE (
  outcome smallint,
  policy text,
  resource text,
  subject text,
  count integer,
  window_started_at timestamp with time zone,
  window_expires_at timestamp with time zone,
  blocked_until timestamp with time zone,
  retain_until timestamp with time zone,
  created timestamp with time zone,
  updated timestamp with time zone,
  is_blocked boolean
)
AS $$
DECLARE
  o_rate_limit register.rate_limit%ROWTYPE;
  o_inserted boolean := FALSE;
  o_count integer;
  o_window_started_at timestamp with time zone;
  o_window_expires_at timestamp with time zone;
  o_blocked_until timestamp with time zone;
BEGIN
  -- Validate input
  ASSERT p_policy IS NOT NULL, 'Policy must be provided';
  ASSERT p_resource IS NOT NULL, 'Resource must be provided';
  ASSERT p_subject IS NOT NULL, 'Subject must be provided';
  ASSERT p_now IS NOT NULL, 'Current timestamp must be provided';
  ASSERT p_cost IS NOT NULL, 'Cost must be provided';
  ASSERT p_cost > 0, 'Cost must be greater than zero';
  ASSERT p_limit IS NOT NULL, 'Limit must be provided';
  ASSERT p_limit > 0, 'Limit must be greater than zero';
  ASSERT p_window_duration IS NOT NULL, 'Window duration must be provided';
  ASSERT p_window_duration > '0 seconds'::interval, 'Window duration must be greater than zero';
  ASSERT p_block_duration IS NOT NULL, 'Block duration must be provided';
  ASSERT p_block_duration > '0 seconds'::interval, 'Block duration must be greater than zero';

  -- Get existing rate-limit entry
  SELECT rl.*
  INTO o_rate_limit
  FROM register.rate_limit rl
  WHERE rl.policy = p_policy
    AND rl.resource = p_resource
    AND rl.subject = p_subject
  FOR NO KEY UPDATE;

  IF NOT FOUND THEN
    BEGIN
      INSERT INTO register.rate_limit(
        policy,
        resource,
        subject,
        count,
        window_started_at,
        window_expires_at,
        blocked_until,
        retain_until,
        created,
        updated
      )
      VALUES (
        p_policy,
        p_resource,
        p_subject,
        p_cost,
        p_now,
        p_now + p_window_duration,
        CASE
          WHEN p_cost >= p_limit THEN p_now + p_block_duration
          ELSE NULL
        END,
        GREATEST(
          p_now + p_window_duration,
          CASE
            WHEN p_cost >= p_limit THEN p_now + p_block_duration
            ELSE '-infinity'::timestamp with time zone
          END),
        p_now,
        p_now
      )
      RETURNING * INTO o_rate_limit;

      o_inserted := TRUE;
    EXCEPTION
      WHEN unique_violation THEN
        SELECT rl.*
        INTO o_rate_limit
        FROM register.rate_limit rl
        WHERE rl.policy = p_policy
          AND rl.resource = p_resource
          AND rl.subject = p_subject
        FOR NO KEY UPDATE;
    END;
  END IF;

  IF NOT o_inserted THEN
    IF o_rate_limit.window_expires_at <= p_now THEN
      o_count := p_cost;
      o_window_started_at := p_now;
      o_window_expires_at := p_now + p_window_duration;
    ELSE
      o_count := o_rate_limit.count + p_cost;
      o_window_started_at := o_rate_limit.window_started_at;
      o_window_expires_at := o_rate_limit.window_expires_at;
    END IF;

    IF o_count >= p_limit
    THEN
      o_blocked_until := GREATEST(
        COALESCE(o_rate_limit.blocked_until, '-infinity'::timestamp with time zone),
        p_now + p_block_duration);
    ELSE
      o_blocked_until := o_rate_limit.blocked_until;
    END IF;

    UPDATE register.rate_limit rl
    SET count = o_count,
        window_started_at = o_window_started_at,
        window_expires_at = o_window_expires_at,
        blocked_until = o_blocked_until,
        retain_until = GREATEST(
          o_window_expires_at,
          COALESCE(o_blocked_until, '-infinity'::timestamp with time zone)),
        updated = p_now
    WHERE rl.policy = p_policy
      AND rl.resource = p_resource
      AND rl.subject = p_subject
    RETURNING * INTO o_rate_limit;
  END IF;

  -- 0:Success { policy: text, resource: text, subject: text, count: integer, window_started_at: timestamp with time zone, window_expires_at: timestamp with time zone, blocked_until: timestamp with time zone, retain_until: timestamp with time zone, created: timestamp with time zone, updated: timestamp with time zone, is_blocked: boolean }
  outcome := 0;
  policy := o_rate_limit.policy;
  resource := o_rate_limit.resource;
  subject := o_rate_limit.subject;
  count := o_rate_limit.count;
  window_started_at := o_rate_limit.window_started_at;
  window_expires_at := o_rate_limit.window_expires_at;
  blocked_until := o_rate_limit.blocked_until;
  retain_until := o_rate_limit.retain_until;
  created := o_rate_limit.created;
  updated := o_rate_limit.updated;
  is_blocked := o_rate_limit.blocked_until IS NOT NULL AND o_rate_limit.blocked_until > p_now;
  RETURN NEXT;
  RETURN;
END;
$$ LANGUAGE plpgsql;
