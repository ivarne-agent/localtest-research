-- Table: register.import_job_party_state
CREATE TABLE register.import_job_party_state(
  job_id text NOT NULL, -- cannot reference import_job.id because import_job is handled in a different transaction
  party_uuid uuid NOT NULL REFERENCES register.party (uuid) ON DELETE CASCADE,
  state_type text NOT NULL,
  state_value jsonb NOT NULL,
  PRIMARY KEY (job_id, party_uuid)
)
TABLESPACE pg_default;
