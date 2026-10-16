CREATE TABLE register.external_main_unit_role (
  source register.external_role_source NOT NULL,
  identifier register.identifier NOT NULL,
  PRIMARY KEY (source, identifier),
  CONSTRAINT external_main_unit_role_source_identifier_fkey FOREIGN KEY ("source", identifier) REFERENCES register.external_role_definition("source", identifier) ON DELETE RESTRICT ON UPDATE RESTRICT
);

INSERT INTO register.external_main_unit_role (source, identifier)
VALUES 
  ('ccr', 'hovedenhet')
, ('ccr', 'ikke-naeringsdrivende-hovedenhet')
, ('ccr', 'administrativ-enhet-offentlig-sektor')
;
