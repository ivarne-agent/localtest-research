-- Returns:
-- | 0:NotFound { policy: text, resource: text, subject: text }
-- | 1:Success { policy: text, resource: text, subject: text, count: integer, window_started_at: timestamp with time zone, window_expires_at: timestamp with time zone, blocked_until: timestamp with time zone, retain_until: timestamp with time zone, created: timestamp with time zone, updated: timestamp with time zone, is_blocked: boolean }
CREATE OR REPLACE FUNCTION register.rate_limit_get_status(
  p_policy text,
  p_resource text,
  p_subject text,
  p_now timestamp with time zone,
  p_blocked_request_behavior register.blocked_request_behavior,
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
BEGIN
  -- Validate input
  ASSERT p_policy IS NOT NULL, 'Policy must be provided';
  ASSERT p_resource IS NOT NULL, 'Resource must be provided';
  ASSERT p_subject IS NOT NULL, 'Subject must be provided';
  ASSERT p_now IS NOT NULL, 'Current timestamp must be provided';
  ASSERT p_blocked_request_behavior IS NOT NULL, 'Blocked request behavior must be provided';
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
    -- 0:NotFound { policy: text, resource: text, subject: text }
    outcome := 0;
    policy := p_policy;
    resource := p_resource;
    subject := p_subject;
    is_blocked := FALSE;
    RETURN NEXT;
    RETURN;
  END IF;

  IF o_rate_limit.blocked_until IS NOT NULL
    AND o_rate_limit.blocked_until > p_now
    AND p_blocked_request_behavior = 'renew'::register.blocked_request_behavior
  THEN
    UPDATE register.rate_limit rl
    SET blocked_until = p_now + p_block_duration,
        retain_until = GREATEST(rl.window_expires_at, p_now + p_block_duration),
        updated = p_now
    WHERE rl.policy = p_policy
      AND rl.resource = p_resource
      AND rl.subject = p_subject
    RETURNING * INTO o_rate_limit;
  END IF;

  -- 1:Success { policy: text, resource: text, subject: text, count: integer, window_started_at: timestamp with time zone, window_expires_at: timestamp with time zone, blocked_until: timestamp with time zone, retain_until: timestamp with time zone, created: timestamp with time zone, updated: timestamp with time zone, is_blocked: boolean }
  outcome := 1;
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
