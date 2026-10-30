# Verification

## Scope and review

Frontend/simulator deleted as authorized, including solution/CI references and Node
pin. Existing backend edits preserved. README is the reviewer guide; AI-Usage
preserves the earlier README. Workflow/spec headers supersede old client-retention
instructions. OpenAPI and docs/api-demo.http are the Part 2 client substitute.

Independent review cross-checked architecture, authorization, locks, events and
metrics against source. TCP readiness command and Admin-only customer route label
were fixed and re-reviewed with no remaining findings.

## Executed checks — 2026-10-29

From repository root:

```powershell
dotnet restore Keyloop.ServiceScheduler.sln
dotnet build Keyloop.ServiceScheduler.sln --no-restore
dotnet test tests/Scheduler.UnitTests --no-build --no-restore --logger 'trx;LogFileName=unit.trx' --results-directory artifacts/reviewer-tests
dotnet test tests/Scheduler.IntegrationTests --no-build --no-restore --logger 'trx;LogFileName=integration.trx' --results-directory artifacts/reviewer-tests
dotnet test tests/Scheduler.IntegrationTests --no-build --no-restore --filter 'FullyQualifiedName~API01_Known_database_unavailability_is_503_and_emits_failed_metric_duration_and_booking_span' --logger 'trx;LogFileName=telemetry-focused.trx' --results-directory artifacts/reviewer-tests
```

| Check | Result |
| --- | --- |
| Restore/build | Passed; zero build warnings/errors |
| Unit | 28 passed, zero failed/skipped |
| Integration | 146 passed, 1 failed, zero skipped (147 total) |
| Focused telemetry | 1 passed; does not erase full-suite failure |
| Independent review | Complete, findings resolved |
| Scoped git diff --check | Passed; line-ending conversion notices only |
| Client dependency search in solution/CI/scripts/tests/backend | No remaining path dependencies |

Parent parsed all three TRX counters. Environment followed README: generated
process-only password, PostgreSQL17.2, port55433, keyloop_scheduler_test_reviewer.
Initial readiness used the earlier socket probe without startup failure; corrected
TCP probe also executed successfully. scheduler-reviewer-tests is stopped with
data preserved. No credentials recorded.

Failure: EnvironmentTests.API01_Known_database_unavailability_is_503_and_emits_failed_metric_duration_and_booking_span
at line74 observed an empty metric collection instead of a `failed` outcome; HTTP
503 assertion passed. This matches the earlier documented intermittent observation.
No tests were weakened or application changes made to hide it.

## Acceptance and limits

- D-01: all requested design sections documented and reviewed.
- D-02/D-03: backend-only build/setup/tests exercised; workflow moved to AI-Usage.
  Full-suite telemetry remains a verification limitation.
- D-04: Swagger/OpenAPI and HTTP examples are the client substitute; the integration
  HTTP harness needs no live identity provider.
- A/Q/ARCH/SEC/DATA/EVT/API regression suites executed; telemetry intermittent.
- D-05: a video outline was retained at the time of this run. Presentation notes
  have since moved outside the repository; recording remains the user's deliverable.

Interrupted Docker task completed all lifecycle assertions in two captured logs;
see ../reviewer-docker/verification.md for outer-wrapper exit limitations. No cloud
CI run, deployment, push or production migration occurred.
