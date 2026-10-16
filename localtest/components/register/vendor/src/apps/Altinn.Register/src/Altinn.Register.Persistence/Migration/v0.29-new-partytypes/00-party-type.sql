-- Enum: register.party_type
-- CREATE TYPE register.party_type AS ENUM(
--   'person',
--   'organization',
--   'self-identified-user'
-- );

ALTER TYPE register.party_type ADD VALUE 'system-user';
ALTER TYPE register.party_type ADD VALUE 'enterprise-user';

-- Enum: register.party_type
-- CREATE TYPE register.party_type AS ENUM(
--   'person',
--   'organization',
--   'self-identified-user',
--   'system-user',
--   'enterprise-user'
-- );
