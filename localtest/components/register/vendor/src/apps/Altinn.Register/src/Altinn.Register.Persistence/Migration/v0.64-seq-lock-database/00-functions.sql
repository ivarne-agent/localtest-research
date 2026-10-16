CREATE OR REPLACE FUNCTION register.tx_max_safeval(
  seq regclass
)
RETURNS bigint AS $$
DECLARE
  seq_id oid;
  database_id oid;
  max_seq bigint;
BEGIN
  -- Make sure seq is a sequence
  SELECT "oid" INTO seq_id
  FROM pg_class
  WHERE "oid" = seq::oid AND "relkind" = 'S';

  IF seq_id IS NULL THEN
    RAISE EXCEPTION 'Relation %s is not a sequence', seq;
  END IF;

  SELECT "oid" INTO database_id
  FROM pg_database
  WHERE datname = current_database();

  -- Find the minimum seq across all running transactions in the current database
  -- note: we deal with two related locks here (correlated by pid and virtualtransaction)
  -- one is to know we've locked on the sequence, and the other is to know what value
  -- we've locked all values after
  -- The first one (seq_lock) is a two-int advisory lock with classid = the oid of the sequence, objid = 0, and objsubid = 2
  -- the second one (val_lock) is a bigint advisory lock split across classid and objid, with objsubid = 1
  SELECT min((val_lock.classid::bigint << 32) | val_lock.objid::bigint) INTO max_seq
  FROM pg_locks seq_lock
  INNER JOIN pg_locks val_lock
    ON  seq_lock.pid = val_lock.pid
    AND seq_lock.virtualtransaction = val_lock.virtualtransaction
    AND seq_lock.database = val_lock.database
  WHERE seq_lock.database = database_id
    AND seq_lock.classid = seq::oid
    AND seq_lock.objid = 0
    AND seq_lock.objsubid = 2
    AND seq_lock.locktype = 'advisory'
    AND seq_lock.granted
    AND val_lock.objsubid = 1
    AND val_lock.locktype = 'advisory'
    AND val_lock.granted;

  -- If no locks are found, return the maximum possible bigint value
  IF max_seq IS NULL THEN
      RETURN 9223372036854775807;
  END IF;

  -- Return the maximum safe value
  RETURN max_seq;
END;
$$ LANGUAGE plpgsql;
