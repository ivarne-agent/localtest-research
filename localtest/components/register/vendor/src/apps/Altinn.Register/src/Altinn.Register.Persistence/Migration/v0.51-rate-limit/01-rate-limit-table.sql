-- Table: register.rate_limit
CREATE TABLE register.rate_limit(
  policy text NOT NULL,
  resource text NOT NULL,
  subject text NOT NULL,
  count integer NOT NULL,
  window_started_at timestamp with time zone NOT NULL,
  window_expires_at timestamp with time zone NOT NULL,
  blocked_until timestamp with time zone NULL,
  retain_until timestamp with time zone NOT NULL,
  created timestamp with time zone NOT NULL,
  updated timestamp with time zone NOT NULL,
  PRIMARY KEY (policy, resource, subject),
  CONSTRAINT rate_limit_count_non_negative CHECK (count >= 0),
  CONSTRAINT rate_limit_window_order CHECK (window_started_at <= window_expires_at),
  CONSTRAINT rate_limit_retain_after_window CHECK (retain_until >= window_expires_at),
  CONSTRAINT rate_limit_retain_after_block CHECK (blocked_until IS NULL OR retain_until >= blocked_until),
  CONSTRAINT rate_limit_updated_after_created CHECK (updated >= created)
)
TABLESPACE pg_default;

CREATE INDEX rate_limit_retain_until_idx
  ON register.rate_limit (retain_until);
