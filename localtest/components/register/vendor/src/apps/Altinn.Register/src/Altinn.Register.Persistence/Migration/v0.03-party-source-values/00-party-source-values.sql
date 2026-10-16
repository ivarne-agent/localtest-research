-- Enum: register.party_source
-- ccr - (Norwegian) Central Coordinating Register for Legal Entities - Enhetsregisteret
ALTER TYPE register.party_source RENAME VALUE 'enhetsregisteret' TO 'ccr';

-- npr - (Norwegian) National Population Register - Folkeregisteret
ALTER TYPE register.party_source RENAME VALUE 'folkeregisteret' TO 'npr';
