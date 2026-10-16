CREATE INDEX ix_from_party
  ON register.external_role_assignment (from_party);

CREATE INDEX ix_to_party
  ON register.external_role_assignment (to_party);

CREATE INDEX ix_from_party_source
  ON register.external_role_assignment (from_party, "source");
