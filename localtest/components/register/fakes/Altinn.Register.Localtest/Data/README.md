# external-role-definitions.json

Snapshot of `register.external_role_definition` plus the `register.external_main_unit_role` flag, produced
by replaying the vendored SQL migrations (`vendor/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration`)
into a throwaway Postgres 16 with `${APP-USER}` and `${YUNIQL-USER}` substituted, then:

```sql
select json_agg(json_build_object(
  'source', d.source, 'identifier', d.identifier, 'code', d.code,
  'name', json_build_object('en', d.name->'en', 'nb', d.name->'nb', 'nn', d.name->'nn'),
  'description', json_build_object('en', d.description->'en', 'nb', d.description->'nb', 'nn', d.description->'nn'),
  'mainUnit', exists(select 1 from register.external_main_unit_role m where m.source = d.source and m.identifier = d.identifier))
  order by d.source, d.identifier)
from register.external_role_definition d;
```

Refresh it whenever a vendored upstream pull adds a migration touching those tables. 103 definitions at
altinn-register f6b5c603 (61 CCR, 42 CRA; 3 main-unit roles).
