CREATE TABLE register.saga_state (
	id uuid NOT NULL PRIMARY KEY,
	status register.saga_status NOT NULL,
	message_ids uuid[] NOT NULL,
	state_type text NOT NULL,
	state_value jsonb NOT NULL,
	created timestamptz NOT NULL,
	updated timestamptz NOT NULL
);
