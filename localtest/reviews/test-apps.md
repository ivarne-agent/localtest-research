# Test apps whose policy or test data production code rejects (2026-10-21)

The monorepo's test apps under `altinn-studio/src/test/apps/` (read at `origin/main` `6e5a6a9920`, 2026-10-21), checked
against what production code in the stack does with them. Not fixed here (`TODO.md` task 12, decision of 2026-10-21):
each finding is for an upstream fix in the monorepo.

## Policies: the Resource Registry's flattener

**The parser.** The Resource Registry's `GET resource/{id}/policy/rules` and `GET resource/{id}/policy/rights` read
the app's `policy.xml` from the policy store and flatten it with `PolicyHelper.ConvertToPolicyRules` →
`FlattenXacmlRule` (`components/resource-registry/vendor/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Helpers/PolicyHelper.cs:278`;
`rights` reaches it through `ConvertToPolicyRight`, line 204). It requires every `AnyOf` of a rule's `Target` to hold
matches of one category only, and throws `ArgumentException("All matches in a all must have the same category ruleId
…")` (line 298) otherwise; the controller does not catch it, so the answer is `500`. It also throws on a rule without
a subject, action or resource `AnyOf` (null reference) and on an action `AllOf` with more than one match; no test app
hits those. The XACML itself is valid: the PDP evaluates these rules as written, so apps run, instantiate and delete
as intended. Only the registry's flattened views fail.

**Not affected:** `policy/subjects?reloadFromXacml=true` (studioctl's provisioning step 3b) collects subject attributes
with `GetAttributeDictionaryByCategory`, not the flattener, so it answers 200 for these policies (seen for
`signing-test` in `../components/resource-registry/README.md`, "Verified"). `v2/resource/{id}/policy/rights` uses
`DelegationCheckHelper.DecomposePolicy`, which skips what it cannot read instead of throwing (read, not run).

**The rule.** In every failing app it is the same template rule, "user with role REGNA or DAGL can delete instances":
its second `AnyOf` has one `AllOf` with the resource matches `urn:altinn:org`, `urn:altinn:app` and the action match
`action-id = delete` together. Production's flattener wants the action in an `AnyOf` of its own, as in the other rules
of the same files. The fix upstream is to move the `delete` match into a third `AnyOf`.

| App | File (`App/config/authorization/policy.xml`) | Rule | Production's reason | What the stack shows |
| --- | --- | --- | --- | --- |
| `stateless-app` | line 160 | `urn:altinn:example:ruleid:3` (delete) | action and resource in one `AnyOf`: `ArgumentException` in `FlattenXacmlRule`, 500 | `policy/rules` 500 for `app_ttd_stateless-app` (seen by the inspection-GUI research, 2026-10-20) |
| `anonymous-stateless-app` | line 118 | `urn:altinn:example:ruleid:3` (delete) | same | 500 expected, not run |
| `component-library` | line 118 | `urn:altinn:example:ruleid:3` (delete) | same | 500 expected, not run |
| `expression-validation-test` | line 118 | `urn:altinn:example:ruleid:3` (delete) | same | 500 expected, not run |
| `payment-test` | line 200 | `urn:altinn:example:ruleid:4` (delete) | same | 500 expected, not run |
| `signering-brukerstyrt` | line 158 | `urn:altinn:org:ttd:signering-brukerstyrt:ruleid:3` (delete) | same | 500 expected, not run |
| `signing-test` | line 263 | `urn:altinn:example:ruleid:3` (delete) | same | 500 expected, not run; step 3b 200 |
| `subform-test` | line 124 | `urn:altinn:example:ruleid:3` (delete) | same | 500 expected, not run |

Pass: `frontend-test`, `multiple-datamodels-test`, `navigation-test-subform`, `process-transition-test`,
`service-task`.

How it was checked: a replay of `FlattenXacmlRule`'s checks (categories per `AnyOf`, the three categories present,
one match per action `AllOf`) in Python over all 13 `policy.xml` files. "500 expected" rows are from that replay and
the code above; `GET http://local.altinn.cloud:8000/resourceregistry/api/v1/resource/app_ttd_<app>/policy/rules` (and
`/policy/rights`) on the stack with the app provisioned confirms them.

**Provisioning is not affected.** studioctl-server's `AppProvisioner` (step 3b, `GET …/policy/subjects?reloadFromXacml=true`)
does not reach the flattener, so it succeeds for all of these. Were it to fail, `Send` throws, the rest of that app's
provisioning (step 4, Access Management's refresh; step 5, the Dialogporten sync) is skipped, and the app is retried
with a warning on every refresh of the app list; nothing else fails.

## Test data

One test app has its own `testData.json`: `signering-brukerstyrt/App/wwwroot/testData.json`. It validates against
`../testdata/test-users.schema.v3.json` (Python `jsonschema`, Draft 2020-12): no errors. It names the v1 schema in
its `$schema`, and validates against v3 all the same. No finding.
