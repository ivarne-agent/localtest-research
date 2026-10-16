-- CREATE TABLE register.party (
-- 	uuid uuid NOT NULL,
-- 	id int8 NULL,
-- 	party_type register.party_type NOT NULL,
-- 	display_name text NOT NULL,
-- 	person_identifier register.person_identifier NULL,
-- 	organization_identifier register.organization_identifier NULL,
-- 	created timestamptz NOT NULL,
-- 	updated timestamptz NOT NULL,
-- 	is_deleted bool DEFAULT false NOT NULL,
-- 	version_id int8 DEFAULT register.tx_nextval('register.party_version_id_seq'::regclass) NOT NULL,
-- 	"owner" uuid NULL,
-- 	CONSTRAINT organization_identifier_check CHECK (((organization_identifier IS NULL) <> (party_type = 'organization'::register.party_type))),
-- 	CONSTRAINT owner_check CHECK (((owner IS NULL) OR (party_type = ANY (ARRAY['system-user'::register.party_type, 'enterprise-user'::register.party_type])))),
-- 	CONSTRAINT party_id_check CHECK (((id IS NULL) <> (party_type = ANY (ARRAY['person'::register.party_type, 'organization'::register.party_type, 'self-identified-user'::register.party_type])))),
-- 	CONSTRAINT party_pkey PRIMARY KEY (uuid),
-- 	CONSTRAINT person_identifier_check CHECK (((person_identifier IS NULL) <> (party_type = 'person'::register.party_type))),
-- 	CONSTRAINT uq_version_id UNIQUE (version_id),
-- 	CONSTRAINT party_owner_fkey FOREIGN KEY ("owner") REFERENCES register.party(uuid) ON DELETE RESTRICT ON UPDATE RESTRICT
-- );

ALTER TABLE register.party
    ADD COLUMN deleted_at timestamptz NULL;

ALTER TABLE register.party
    ADD CONSTRAINT deleted_at_requires_deleted_check CHECK ((deleted_at IS NULL) OR (is_deleted = true));
