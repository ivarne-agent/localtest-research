-- Enum: register.party_type
CREATE TYPE register.party_type AS ENUM(
  'person',
  'organization'
);

-- Enum: register.party_source
CREATE TYPE register.party_source AS ENUM(
  'folkeregistered',
  'enhetsregisteret'
);
