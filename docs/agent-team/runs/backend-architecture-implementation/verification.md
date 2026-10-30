# Verification

## Final result — 2026-10-28

Backend implementation and required local executable checks are complete. Final
working tree is uncommitted, based on `7def013b6c1fb51a1903a1c96c5a4378208cf93d`,
and includes preserved pre-existing documentation/PDF/IDE changes. This report does
not attribute the entire dirty diff to the feature. Submission video D-05 remains
outstanding. One earlier intermittent telemetry observation remains unexplained.

- Solution build: passed; one NU1900 warning because vulnerability-feed access was unavailable.
- Unit: 28/28 passed, no skips (`final-unit-05.trx`).
- Integration: 140/140 passed, no skips (`final-integration-06.trx`), using real PostgreSQL 17.2 and real JWT bearer validation with synthetic keys.
- Repeated concurrency: three runs, each 2/2 passed, no skips (`final-concurrency-06-1.trx` through `-3.trx`). Each run covers 20 requests against one- and two-pair capacity.
- Independent review: no concrete remaining defects. Narrow simplification review: no change warranted.
- Final source/test/documentation whitespace check passed, with line-ending notices only.

### Acceptance matrix

| IDs | Evidence | Result |
| --- | --- | --- |
| A-01, A-03 | BookingTests persisted booking across a new host; OwnershipHttpTests customer booking/read/list | Pass |
| A-02, Q-02 | BookingTests full-duration resource/qualification/opening-hours and independent overlap cases | Pass |
| Q-01 | BookingTests invalid/past/ownership input; server duration; domain/application validation | Pass |
| Q-03 | BookingTests touching boundaries and independent exclusion cases | Pass |
| Q-04 | Final full-suite and repeated capacity races; stale-candidate application regression; forced raw-writer exclusion loser | Pass |
| Q-05, DATA-01 | ProviderTransactionTests constraints, rollback, isolation, durability settings, unknown commit; BookingTests fresh-host readback | Pass |
| Q-06 | README, system-design.md, backend-architecture.md: UTC, hours, immutable intervals, JWT/error contracts | Documented |
| ARCH-01, ARCH-02 | AppointmentTests, ArchitectureTests, independent boundary review | Pass |
| ARCH-03 | ProviderTransactionTests untracked non-reserving queries; separate handlers | Pass |
| SEC-01 | BearerTests real signature/issuer/audience/lifetime/claims/policy checks | Pass |
| SEC-02 | OwnershipHttpTests trusted identity mapping, spoofing/concealment, Admin/dual role | Pass |
| EVT-01 | ApplicationBehaviorTests, EventDispatcherTests, CommittedEventHttpTests and ProviderTransactionTests commit/rollback/cleanup/timeout behavior | Pass |
| API-01 | Binding/ProblemDetails assertions, bearer challenges, sanitized errors/logs and EnvironmentTests | Pass on final run; intermittent telemetry caveat below |
| TEST-01 | Final TRX artifacts, independent tester/reviewer and real provider evidence | Pass with recorded caveat |
| D-01 | system-design.md and backend-architecture.md: diagrams, roles, data flow, rationale, observability, GenAI narrative | Present |
| D-02 | Existing Git repository, implemented backend, migrations and executed business/provider tests | Present, uncommitted |
| D-03 | README setup/run/test instructions and actual AI collaboration narrative | Present |
| D-04 | Authenticated HTTP examples, Swagger bearer setup and executable HTTP integration flows | Present |
| D-05 | An outline was present at the time of this run; presentation notes are now maintained outside the repository | Outstanding: user records final video |

### Final executed commands

Working directory: repository root. Test environment used the existing isolated
loopback PostgreSQL 17.2 instance, owned test database and synthetic test role
documented below. Set `ConnectionStrings__SchedulerTestDb` privately to that test
instance and `SchedulerTests__PostgresPort=55432`; no secret is recorded here.

```powershell
dotnet build Keyloop.ServiceScheduler.sln --no-restore
dotnet test tests/Scheduler.UnitTests --no-build --no-restore --logger 'trx;LogFileName=final-unit-05.trx' --results-directory artifacts/backend-architecture-implementation
dotnet test tests/Scheduler.IntegrationTests --no-build --no-restore --logger 'trx;LogFileName=final-integration-06.trx' --results-directory artifacts/backend-architecture-implementation
foreach ($run in 1..3) {
    dotnet test tests/Scheduler.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~AC10_11_Concurrent_clients_use_all_and_only_available_capacity --logger "trx;LogFileName=final-concurrency-06-$run.trx" --results-directory artifacts/backend-architecture-implementation
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
git diff --check -- src/backend tests README.md docs specs START_HERE.txt .github/workflows/verify.yml
```

### Remaining limits

The first final full run passed 139/140: its unavailable-database request correctly
returned 503, but the test observed no failed metric. The unchanged test passed
isolated, in two focused groups and in the full rerun. No root cause or fix is
claimed; keep the earlier TRX as evidence of possible telemetry/test intermittency.
The database deadlock was separately reproduced, fixed and repeatedly verified.

External production identity-provider configuration, real Customer identity links
and demo database setup remain operator inputs. No live identity-provider test,
remote CI execution, deployment, production migration, throughput benchmark or
power-loss durability test is claimed. Optional React/simulator clients were not
expanded or verified against authenticated routes. In-memory events remain lossy,
and allocation locks serialize attempts sharing resources, including disjoint times.

The historical notes below retain failed attempts and intermediate states; the
final result above supersedes their pending-status statements.

## Executed baseline
- dotnet --version: 8.0.407 (pinned).
- dotnet build Keyloop.ServiceScheduler.sln --no-restore: PASS, 0 warnings/errors.
- dotnet test tests/Scheduler.UnitTests --no-build --no-restore --logger trx;LogFileName=baseline-unit.trx --results-directory artifacts/backend-architecture-implementation: PASS, 10/10, no skips.
- Integration baseline pending final counts; provider setup reports missing ConnectionStrings__SchedulerTestDb. No provider success claimed.

Artifacts: artifacts/backend-architecture-implementation/. No credentials recorded.

Baseline integration command completed: 40 total, 5 passed, 35 failed, 0 skipped. 33 failed at missing test DB prerequisite; two existing environment checks returned 404 instead of 500 and readiness 200 instead of 503. These are pre-existing failures, not provider success.

## Early independent review (source inspection, not executed proof)

- Medium: diagnostic MeterListener callbacks can throw after commit in BookAppointmentHandler; developer assigned isolation, tester assigned regression.
- Low: Eligible materializes Cartesian resource pairs rather than baseline lazy selection; developer assigned bounded improvement without changing allocation behavior.
- Parent findings assigned: known-commit cleanup errors must preserve success; remove arbitrary retry count that can reject remaining capacity; sanitize config and keep provider classification in Infrastructure.

Reviewer final integrated review remains pending. No review findings are marked resolved until rechecked.

## Provider prerequisite resolved without a version exception

- Docker28.0.1 available after sandbox escalation; pulled postgres:17.2 (digest sha256:3267c505060a0052e5aa6e5175a7b41ab6b04da2f8c4540fc6e98a37210aa2d3).
- Started keyloop-scheduler-architecture-test-20261027 bound only127.0.0.1:55432; existing localhost5432 server untouched.
- Isolated database keyloop_scheduler_test_architecture_20261027; created non-superuser/non-createdb/non-createrole scheduler_test login as database owner.
- psql checks: server17.2 (Debian17.2-1.pgdg120+1), fsync=on, synchronous_commit=on.
- Container uses trust authentication solely for this disposable loopback-bound test instance; no application credentials introduced. It is not a production setup recommendation.
- Explicit test-port override planned/default remains5432; server version default remains17.2. The earlier17.4 compatibility question is no longer required for this run.
- These are prerequisite checks, not yet booking/provider acceptance results.

## Developer handoff (executed by IMPL-CORE)

- dotnet build src/backend/Scheduler.Api/Scheduler.Api.csproj --no-restore: passed, 0 errors, one NU1900 vulnerability-feed warning.
- dotnet ef migrations add CustomerIdentityLinks --project src/backend/Scheduler.Infrastructure --startup-project src/backend/Scheduler.Api --no-build -- --ConnectionStrings:SchedulerDb="Host=127.0.0.1;Port=1;Database=scaffolding_only": succeeded (no database connection/mutation).
- dotnet ef migrations has-pending-model-changes with the same project/startup/no-build options: no model drift.
- git diff --check -- src/backend: passed; line-ending notices only.
- Tester now owns serialized build/test execution slot. Production verification/review still pending.

## Integrated review round1

- Early metric/activity callback isolation and lazy pair enumeration rechecked in source: resolved, pending runtime regression results.
- Medium: design-time factory ignored configured connection; assigned private configuration loading while retaining connection-free scaffolding.
- Medium: Infrastructure swallowed cleanup failure and incorrectly allowed postcommit dispatch; assigned propagate-after-both-cleanups semantics so handler preserves confirmation but skips diagnostics on uncertain cleanup.
- Tester assigned conservative event-suppression coverage for cleanup failure before and after base disposal, plus setup-path verification. Production owner edits only while tester holds execution slot; next builds wait for saved fixes.

## Independent tester first execution (TRX inspected by parent)

- impl-test-unit-01.trx:28/28 passed,0 failed/skipped.
- impl-test-integration-01.trx:125/128 passed,3 failed,0 skipped. Includes real PostgreSQL17.2 provider cases; failures: empty subject/role claim configuration returned500 instead of401 (two theory cases), and dependency-unavailability HTTP returned500 instead of503.
- Failures assigned for correction; these artifacts are retained, not overwritten by later runs. Counts parsed directly from TRX Counters, not inferred from source.

Dependency diagnostic02 reproduced exception type chain System.InvalidOperationException -> Npgsql.NpgsqlException -> System.TimeoutException. Narrow Infrastructure wrapper classification assigned; no automatic retry policy added.

## Continuation audit — 2026-10-28

- Preserved all existing implementation, documentation, test and IDE changes.
- Recovered previously unrecorded `impl-test-unit-03.trx`: 28/28 passed; `impl-test-integration-03.trx`: 138/139 passed. The remaining failure expected 19 conflict responses in the single-capacity concurrency case; it observed zero. This is unresolved until current provider execution explains it.
- Independent review rechecked the integrated production/test source and prior fixes; no concrete remaining source defects found. The new concurrency unexpected-status/body diagnostic assertion was separately reviewed and preserves all prior assertions.
- Current build passed with one NU1900 vulnerability-feed warning. `resume-unit-04.trx`: 28/28 passed. `resume-nonprovider-04.trx`: 71/71 passed (architecture, application, bearer and dispatcher tests).
- Docker Desktop was stopped at continuation. Started Desktop and the existing isolated test container with approval; the normal local database remains untouched. Provider revalidation is in progress.
- Main-session documentation diff whitespace check passed (line-ending notices only). Optional simulator commands were removed from the secured-backend instructions because that client still lacks bearer support.
- `resume-concurrency-04.trx`: 1/2 passed. The two-capacity case returned eighteen HTTP 500 responses. Docker logs confirmed mutual transaction ShareLock deadlocks during appointment exclusion checks. Q-04/DATA-01 remain failing; no completion claimed. User requested GPT-6-Luna with medium reasoning for the database fix; delegated production ownership accordingly.

## Allocation-lock correction

GPT-6-Luna (medium) added an Application allocation-lock port implemented with
parameterized PostgreSQL transaction-scoped advisory locks on the selected bay
and technician in consistent order. The handler checks eligibility again after
waiting; a stale candidate disposes its transaction before trying remaining pairs.
Independent exclusion constraints and non-retryable unknown commit outcomes remain
unchanged. Locks serialize even nonoverlapping bookings sharing a resource while
the attempt runs; no throughput benchmark is claimed.

Independent reviewer inspected the source fix and the new stale-candidate test:
no concrete defects; `git diff --check -- src/backend tests` passed. The test proves
that a stale attempt neither saves nor commits and that a fresh alternative attempt
publishes one event. Tester owns final execution. Developer and tester hit a usage
limit after saving work; continuation resumed the same tester.

- Final solution build passed with one NU1900 vulnerability-feed warning.
- `final-unit-05.trx`: 28/28 passed.
- `final-integration-05.trx`: 139/140 passed, no skips. Both real-provider concurrency
  cases passed. One unavailable-database telemetry test observed no failed metric;
  its HTTP 503 assertion passed. Diagnosis/revalidation remains pending.
- That telemetry test passed isolated (1/1), with ApplicationBehaviorTests (29/29),
  and with EnvironmentTests (8/8); artifacts are `final-*-06.trx`. These do not
  establish a root cause for the earlier full-suite failure.
