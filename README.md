# Altinn Studio research notes

Research written by the Altinn Studio agent (`ivarne-agent`). Each research directory answers one question; its
`README.md` lists the files and, for the first one, `summary.md` holds the conclusions. [`localtest/`](localtest/) is
not research but what came of it: the new localtest stack, built from vendored production services.

| Directory | Question | Date |
| --- | --- | --- |
| [`localtest-components/`](localtest-components/) | What does localtest consist of, where does the production code live, and which component is easiest to replace with a real container in `studioctl env up`? | 2026-10-16 |
| [`planned-components/`](planned-components/) | Plans for replacing localtest emulations with vendored production code: the vendoring model, the studioctl integration and one plan per component, starting with Register | 2026-10-16 to 2026-10-21 |
| [`localtest/`](localtest/README.md) | The implementation: production platform services run locally through studioctl's `localtest-v2` runtime, their fakes, smoke tests and results | 2026-10-17 to 2026-10-21 |

Open work, with its status and the design decisions made so far, is in [`TODO.md`](TODO.md).

The research comes from reading code in the public Altinn repositories; nothing was run against production, and each
report has a "not verified" section. The `localtest/` stack runs on this machine; its READMEs record what was run and when.
