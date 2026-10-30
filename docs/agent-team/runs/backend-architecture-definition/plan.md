# Plan

ARCH-ANALYZE: read-only spec_analyst inspects source and gaps; complete.
ARCH-DESIGN: main session owns backend-architecture.md, spec, project/workflow,
system-design, README, START_HERE and these records; no application write scope.
ARCH-REVIEW: independent constraint_reviewer reviews documentation only.

Future execution order and testing gates are in backend-architecture.md. Assign
bounded Domain/Application, Infrastructure, API/security and test ownership at that
time; serialize shared migration/contracts/composition changes. No code work now.

## Current orchestration pass: staged implementation handoff

Implementation requires a later explicit request and a separate run. At most three
agents may be active. The main session alone owns documentation and integration.
The tasks below are planned, not started; paths denote exclusive future ownership.

| Stage / IDs | Owner and write scope | Dependency and exit gate |
| --- | --- | --- |
| S0 baseline / TEST-01 | Main: new run records; tester: read-only commands | Inspect dirty tree, SDK/packages and test discovery; record actual build/unit/provider baseline and prerequisites before changes |
| S1 domain and ports / ARCH-01..03 | One developer: Scheduler.Domain/**, Scheduler.Application/**, focused unit tests | Preserve BookingRules; define aggregate creation, UTC interval, typed command/query results, current-user, read, transaction and event ports; freeze shared contracts |
| S2 persistence / DATA-01, A-02..03, Q-04..05 | One persistence developer: Scheduler.Infrastructure/** including all migrations/model snapshot | S1 ports fixed; adapt existing SQL/constraints and allocation logic, fresh context/transaction per retry, identity-link mapping; real provider regression gate |
| S3 authentication and HTTP / SEC-01..02, API-01 | One API developer: Scheduler.Api/** and API project package references | S1/S2; JWT configuration, trusted current-user adapter, policies, handler routing, consistent errors/logs; real bearer tests |
| S4 events / EVT-01 | S1 owner: Application event orchestration; S2 owner: Infrastructure dispatcher, sequential handoff | S2/S3; commit outcome known before dispatch; rollback/loser suppression and subscriber failure/timeout checks |
| S5 independent verification / all executable IDs | Tester: tests/Scheduler.UnitTests/** and tests/Scheduler.IntegrationTests/** after developer relinquishes test ownership | Integrated snapshot frozen; PostgreSQL/JWT/concurrency/event checks plus HTTP smoke; save commands, counts, failures/skips and sanitized evidence |
| S6 independent review / all IDs | constraint_reviewer: read-only; main: docs/** and README.md | Review exact integrated snapshot and tester evidence; fixes return to owners; rerun affected tests; document D-01..05 gaps |

One named developer owns Infrastructure migrations/model changes throughout.
One owns shared Application contracts; one owns API composition and package changes.
Application ownership includes handler role/ownership enforcement in S3; transfer
that bounded work back to its owner rather than allowing concurrent edits.
No simultaneous writers in shared tests. Parallelize only disjoint work after ports
are agreed; serialize builds/tests that share output directories, ports or databases.

## Future execution gates

- Inspect the existing test harness before execution: TestDatabase.InitializeAsync
  automatically applies migrations. Do not run it during this design phase.
- Later tests require the private SchedulerTestDb connection setting and an isolated
  allowlisted local database. Never fall back to demo storage or print credentials.
  Preserve the 17.2 version check. Historical 17.4 observations are not a current
  environment probe; obtain an explicit compatibility decision only if reconfirmed.
- Planned commands: `dotnet build Keyloop.ServiceScheduler.sln --no-restore`,
  `dotnet test tests/Scheduler.UnitTests --no-restore`, and
  `dotnet test tests/Scheduler.IntegrationTests --no-restore`. Missing restored
  assets are a prerequisite failure, not permission to install in this phase.
  Later capture TRX results, discovered/executed counts and provider identification;
  a successful command with skipped provider cases does not satisfy TEST-01.
- Real bearer tests configure synthetic issuer/audience and ephemeral signing keys
  on the actual bearer handler; do not replace authentication with fake principals.
  Update existing HTTP fixtures to send authorized tokens so booking tests reach
  their intended behavior instead of stopping at 401.
- Concurrency tests use a start barrier, independent requests/contexts and database
  readback. Force a first-choice collision to prove retry/alternative allocation;
  incidental concurrent success alone does not prove that branch executed.
- Post-commit subscriber tests independently read committed state. Inject rollback,
  known exclusion loss, subscriber exception and cooperative timeout; verify row
  counts, event identities and successful committed outcomes. No durable replay claim.
- Independent tester and reviewer must report blockers honestly. React/simulator
  work remains optional; no scaffold, deployment, push or production migration.
