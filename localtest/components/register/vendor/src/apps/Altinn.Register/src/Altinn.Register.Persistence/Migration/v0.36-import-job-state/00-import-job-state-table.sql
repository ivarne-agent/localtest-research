-- Table: register.import_job_state
CREATE TABLE register.import_job_state(
  job_id text NOT NULL, -- cannot reference import_job.id because import_job is handled in a different transaction
  state_type text NOT NULL,
  state_value jsonb NOT NULL,
  PRIMARY KEY (job_id)
)
TABLESPACE pg_default;
