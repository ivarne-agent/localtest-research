-- Domain: register.person_identifier
CREATE DOMAIN register.person_identifier AS text CONSTRAINT fmtchk CHECK (value ~ '^[0-9]{11}$');

-- Domain: register.organization_identifier
CREATE DOMAIN register.organization_identifier AS text CONSTRAINT fmtchk CHECK (value ~ '^[0-9]{9}$');
