CREATE TYPE register.organization_source AS ENUM (
	'ccr',  -- ccr - (Norwegian) Central Coordinating Register for Legal Entities - Enhetsregisteret
	'sdf' -- sdf - Businesses assessed as partnerships - Skatteetaten-registrerte selskaper (Selskap med deltakerfastsetting)
);

-- CREATE TABLE register.organization (
-- 	"uuid" uuid NOT NULL,
-- 	unit_status text NULL,
-- 	unit_type text NULL,
-- 	telephone_number text NULL,
-- 	mobile_number text NULL,
-- 	fax_number text NULL,
-- 	email_address text NULL,
-- 	internet_address text NULL,
-- 	mailing_address register.mailing_address NULL,
-- 	business_address register.mailing_address NULL,
-- 	CONSTRAINT organization_pkey PRIMARY KEY (uuid),
-- 	CONSTRAINT organization_uuid_fkey FOREIGN KEY ("uuid") REFERENCES register.party("uuid") ON DELETE CASCADE ON UPDATE CASCADE
-- );

ALTER TABLE register.organization
  ADD COLUMN source register.organization_source;

UPDATE register.organization
   SET source = CASE
       WHEN p.organization_identifier::text LIKE '0%' THEN 'sdf'::register.organization_source
       ELSE 'ccr'::register.organization_source
   END
  FROM register.party p
 WHERE p.party_type = 'organization'::register.party_type
   AND p.uuid = organization.uuid;

ALTER TABLE register.organization
  ALTER COLUMN source SET NOT NULL;
