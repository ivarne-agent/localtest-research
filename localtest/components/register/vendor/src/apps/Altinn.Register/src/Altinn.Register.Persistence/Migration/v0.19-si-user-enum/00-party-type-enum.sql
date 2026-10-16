-- Enum: register.party_type
-- CREATE TYPE register.party_type AS ENUM(
--   'person',
--   'organization'
-- );

ALTER TYPE register.party_type ADD VALUE 'self-identified-user';

-- Enum: register.party_type
-- CREATE TYPE register.party_type AS ENUM(
--   'person',
--   'organization',
--   'self-identified-user'
-- );
