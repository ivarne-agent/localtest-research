CREATE TYPE register.saga_status AS ENUM (
  'in_progress',
  'completed',
  'faulted'
 );
