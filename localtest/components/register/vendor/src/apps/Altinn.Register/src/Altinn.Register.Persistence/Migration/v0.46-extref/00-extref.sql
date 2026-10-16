-- CREATE TABLE register.party (
-- 	"uuid" uuid NOT NULL,
-- 	id int8 NULL,
-- 	"party_type" register."party_type" NOT NULL,
-- 	display_name text NOT NULL,
-- 	"person_identifier" register."person_identifier" NULL,
-- 	"organization_identifier" register."organization_identifier" NULL,
-- 	created timestamptz NOT NULL,
-- 	updated timestamptz NOT NULL,
-- 	is_deleted bool DEFAULT false NOT NULL,
-- 	version_id int8 DEFAULT register.tx_nextval('register.party_version_id_seq'::regclass) NOT NULL,
-- 	"owner" uuid NULL,
-- 	deleted_at timestamptz NULL
-- );

ALTER TABLE register.party
  ADD COLUMN ext_urn text;

-- CREATE TEMP TABLE party_ext_urn (
--   uuid uuid PRIMARY KEY NOT NULL,
--   ext_urn text NOT NULL
-- ) ON COMMIT DROP;

-- INSERT INTO party_ext_urn (uuid, ext_urn)
-- SELECT
--   p.uuid,
--   CASE
--     WHEN p.party_type = 'person' THEN CONCAT('urn:altinn:person:identifier-no:', p.person_identifier::text)
--     WHEN p.party_type = 'organization' THEN CONCAT('urn:altinn:organization:identifier-no:', p.organization_identifier::text)
--     WHEN p.party_type = 'system-user' THEN CONCAT('urn:altinn:systemuser:uuid:', p.uuid::text)
--     ELSE NULL
--   END AS ext_urn
-- FROM register.party p
-- WHERE p.party_type IN ('person', 'organization', 'system-user')
--   AND p.ext_urn IS NULL;

-- UPDATE register.party p
-- SET ext_urn = pe.ext_urn
-- FROM party_ext_urn pe
-- WHERE p.uuid = pe.uuid;

ALTER TABLE register.party
  ADD CONSTRAINT chk_party_ext_urn CHECK (
    CASE
      WHEN party_type = 'person' THEN ext_urn = CONCAT('urn:altinn:person:identifier-no:', "person_identifier"::text)
      WHEN party_type = 'organization' THEN ext_urn = CONCAT('urn:altinn:organization:identifier-no:', "organization_identifier"::text)
      WHEN party_type = 'system-user' THEN ext_urn = CONCAT('urn:altinn:systemuser:uuid:', "uuid"::text)
      WHEN party_type = 'enterprise-user' THEN ext_urn IS NULL
      WHEN party_type = 'self-identified-user' THEN TRUE -- Multiple formats, some NULL for self-identified users
    END
  )
  NOT VALID;

CREATE UNIQUE INDEX uq_party_ext_urn ON register.party (ext_urn);
