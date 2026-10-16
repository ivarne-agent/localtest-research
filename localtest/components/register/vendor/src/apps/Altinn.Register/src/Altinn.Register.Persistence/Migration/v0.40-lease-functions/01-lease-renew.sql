-- Returns:
-- | 0:Success { id: text, token: uuid, expires: timestamp with time zone, acquired: timestamp with time zone }
-- | 1:WrongToken { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
-- | 2:LeaseNotFound { id: text }
-- | 3:LeaseExpired { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
CREATE OR REPLACE FUNCTION register.lease_renew(
  p_id text,
  p_token uuid,
  p_now timestamp with time zone,
  p_expires timestamp with time zone
)
RETURNS TABLE (
  outcome smallint,
  id text,
  token uuid,
  expires timestamp with time zone,
  acquired timestamp with time zone,
  released timestamp with time zone
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
    -- If the lease does not already exist, we can't renew it
    -- 2:LeaseNotFound { id: text }
    outcome := 2;
    id := p_id;
    RETURN NEXT;
    RETURN;

  ELSIF o_lease.token != p_token THEN
    -- If the provided token does not match the existing lease token, we can't renew it
    -- 1:WrongToken { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
    outcome := 1;
    id := p_id;
    acquired := o_lease.acquired;
    expires := o_lease.expires;
    RETURN NEXT;
    RETURN;

  ELSIF o_lease.expires < p_now THEN
    -- The lease is expired and cannot be renewed
    -- 3:LeaseExpired { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
    outcome := 3;
    id := p_id;
    acquired := o_lease.acquired;
    expires := o_lease.expires;
    RETURN NEXT;
    RETURN;

  ELSE
    -- The lease is valid and can be renewed
    UPDATE register.lease l
    SET token = gen_random_uuid(),
        expires = p_expires
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
