-- Returns:
-- | 0:Success { id: text, token: uuid, expires: timestamp with time zone, acquired: timestamp with time zone }
-- | 1:ConditionUnmet { id: text, condition: text, acquired: timestamp with time zone, released: timestamp with time zone }
-- | 2:LeaseUnavailable { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
CREATE OR REPLACE FUNCTION register.lease_acquire(
  p_id text,
  p_now timestamp with time zone,
  p_expires timestamp with time zone,
  p_condition_released_before timestamp with time zone
)
RETURNS TABLE (
  outcome smallint,
  id text,
  token uuid,
  expires timestamp with time zone,
  acquired timestamp with time zone,
  released timestamp with time zone,
  condition text
)
AS $$
DECLARE
  o_lease register.lease%ROWTYPE;
BEGIN
  -- Validate input
  ASSERT p_id IS NOT NULL, 'Lease ID must be provided';
  ASSERT p_now IS NOT NULL, 'Current timestamp must be provided';
  ASSERT p_expires IS NOT NULL, 'Expiration timestamp must be provided';

  -- Get existing lease entry
  SELECT l.*
  INTO o_lease
  FROM register.lease l
  WHERE l.id = p_id
  FOR NO KEY UPDATE;

  IF NOT FOUND THEN
    -- If the lease does not already exist, we can acquire it unconditionally
    INSERT INTO register.lease (id, token, expires, acquired, released)
    VALUES (p_id, gen_random_uuid(), p_expires, p_now, NULL)
    RETURNING * INTO o_lease;

  ELSIF o_lease.expires >= p_now THEN
    -- The lease exists and is not expired
    -- 2:LeaseUnavailable { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
    outcome := 2;
    id := p_id;
    acquired := o_lease.acquired;
    expires := o_lease.expires;
    RETURN NEXT;
    RETURN;

  ELSIF o_lease.expires < p_now THEN
    -- If the lease exists, but is expired, we can only acquire it if the conditions are met
    IF p_condition_released_before IS NOT NULL AND p_condition_released_before < o_lease.released THEN
      -- 1:ConditionUnmet { id: text, condition: text, acquired: timestamp with time zone, released: timestamp with time zone }
      outcome := 1;
      id := p_id;
      condition := 'released_before';
      acquired := o_lease.acquired;
      released := o_lease.released;
      RETURN NEXT;
      RETURN;
    END IF;

    -- Acquire the expired lease
    UPDATE register.lease l
    SET token = gen_random_uuid(),
        expires = p_expires,
        acquired = p_now,
        released = NULL
    WHERE l.id = p_id
    RETURNING * INTO o_lease;
  END IF;

  -- If we've reached here, we've acquired/renewed/released the lease successfully
  -- 0:Success { id: text, token: uuid, expires: timestamp with time zone, acquired: timestamp with time zone }
  outcome := 0;
  id := o_lease.id;
  token := o_lease.token;
  expires := o_lease.expires;
  acquired := o_lease.acquired;
  RETURN NEXT;
  RETURN;
END;
$$ LANGUAGE plpgsql;
