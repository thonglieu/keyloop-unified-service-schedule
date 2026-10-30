# Verification

Latest recorded verification: **2026-10-30**.

This document summarizes existing evidence for the current backend submission:
ASP.NET Core API, PostgreSQL persistence, JWT authorization, Docker setup, and
Swagger/OpenAPI with HTTP examples. It was updated from the linked verification
reports only; no builds or tests were rerun for this documentation update.

## Recorded result

The latest Docker test run passed **187/187 tests**: **28 unit tests** and
**159 integration tests**, with zero failures or skips, using PostgreSQL **17.2**.
The image build and test command completed with exit code 0. Build layers were
cached; this is not a claim of a fresh dependency restore.

The October 29 local solution build also passed with zero warnings or errors.
Reviewer startup, authentication, restart persistence, and migration failure guards
have recorded successful checks. Remote GitHub Actions execution and the final
submission video remain unverified or outstanding.

## Acceptance mapping

The IDs below follow [the feature specification](../specs/feature.md). Detailed
case-level evidence is retained in the linked reports.

| Criteria | Recorded coverage | Result |
| --- | --- | --- |
| A-01, A-03 | Persisted bookings, customer booking/read/list, fresh-host readback | Passed |
| A-02, Q-01-Q-03 | Input validation, full-duration availability, qualifications, opening hours, independent resource overlap and touching boundaries | Passed |
| Q-04 | Concurrent booking capacity, stale-candidate handling, independent exclusion constraints | Passed; repeated capacity races also recorded on October 28 |
| Q-05, DATA-01 | Real PostgreSQL transactions, rollback, isolation, durability settings, unknown commit outcomes and readback | Passed within tested scenarios |
| Q-06 | UTC, opening hours, immutable intervals, JWT and error contracts | Documented |
| ARCH-01-ARCH-03 | Domain/application boundaries, separate handlers and non-reserving queries | Passed |
| SEC-01, SEC-02 | JWT signature/issuer/audience/lifetime validation, trusted customer identity, ownership and Admin authorization | Passed |
| EVT-01 | Commit/rollback event behavior and cleanup, timeout and dispatcher failure paths | Passed |
| API-01 | Binding, ProblemDetails, bearer challenges, sanitized errors and dependency-failure telemetry | Passed in latest full suite; see telemetry note below |
| TEST-01 | Unit, architecture, application, HTTP, provider, concurrency and event-failure checks | Latest recorded suite: 187 passed, zero failed/skipped |
| D-01 | Architecture, data flow, rationale, observability and AI design-assistance documentation | Present and reviewed |
| D-02, D-03 | Backend source, migrations, build/setup/test instructions and AI collaboration narrative | Local build, setup and test evidence recorded; remote repository/CI not verified here |
| D-04 | Swagger/OpenAPI and authenticated HTTP examples | Present; HTTP integration flows tested |
| D-05 | Final recorded demonstration video, delivered separately | Outstanding; presentation notes are maintained outside this repository |

## Executed checks and evidence

These commands and results are taken from existing reports. Commands were run
from the repository root; database credentials are intentionally omitted.

| Date | Check | Recorded outcome | Evidence |
| --- | --- | --- | --- |
| 2026-10-28 | Backend build, unit/provider suites and three repeated concurrency runs | Build passed; 28/28 unit, 140/140 integration and 2/2 concurrency cases per repetition passed | [Backend implementation](agent-team/runs/backend-architecture-implementation/verification.md) |
| 2026-10-29 | `dotnet build Keyloop.ServiceScheduler.sln --no-restore` | Passed, zero warnings/errors | [Reviewer setup](agent-team/runs/reviewer-simple-setup/verification.md) |
| 2026-10-29 | Unit suite and final PostgreSQL 17.2 integration suite | 28/28 unit and 159/159 integration passed | [Reviewer setup](agent-team/runs/reviewer-simple-setup/verification.md) |
| 2026-10-29 | `scripts/start-reviewer.ps1` and live HTTP smoke checks | Startup, three JWT accounts, Admin fixtures and customer-owned vehicles passed; repeated startup preserved configuration and fixture namespace | [Reviewer setup](agent-team/runs/reviewer-simple-setup/verification.md) |
| 2026-10-29 | Reviewer lifecycle script | Final run exited 0: invalid credentials rejected, migration/seed/readiness passed, three appointments survived restart, migration guard prevented API startup | [Reviewer setup](agent-team/runs/reviewer-simple-setup/verification.md) |
| 2026-10-30 | `docker compose -f compose.tests.yaml -p keyloop-scheduler-tests config --quiet` | Passed, with sandbox Docker configuration access warnings | [Docker test run](agent-team/runs/reviewer-compose-tests/verification.md) |
| 2026-10-30 | `docker compose -f compose.tests.yaml -p keyloop-scheduler-tests up --build --abort-on-container-exit --exit-code-from tests` | Exit 0; PostgreSQL 17.2; 28/28 unit and 159/159 integration passed | [Docker test run](agent-team/runs/reviewer-compose-tests/verification.md) |
| 2026-10-30 | `docker compose -f compose.tests.yaml -p keyloop-scheduler-tests down` | Exit 0; isolated containers and network removed; exported results preserved | [Docker test run](agent-team/runs/reviewer-compose-tests/verification.md) |

Latest exported test results:

- `artifacts/reviewer-compose-tests/unit.trx`
- `artifacts/reviewer-compose-tests/integration.trx`

The Docker test report records inspection of both TRX counters after cleanup:
187 total, zero failures and zero skips. Tests used a fresh temporary database
without a published database port or the demo database volume.

## Findings and limits

- Earlier October 28-29 runs exposed an intermittent missing telemetry metric.
  The October 29 setup report records fault-injection callback cleanup, focused
  reproduction checks, and a final 159/159 integration pass. The October 30 Docker
  run also passed all 159 integration tests. Earlier failed runs are not counted
  as successful runs.
- Independent reviews recorded no remaining blocking defects for backend setup
  and the Docker test workflow.
- Docker runner failure propagation was reviewed in source; deliberate failure
  injection into that runner was not executed.
- Bash startup runtime execution, a live external identity provider, remote
  GitHub Actions execution, production deployment/migrations, throughput benchmarks
  and power-loss durability testing are not established by these reports.
- In-memory events are not durable. Allocation locks serialize attempts sharing
  resources, including attempts for disjoint time intervals.
- The final demonstration video remains the user's deliverable.

For current setup and test instructions, use [README](../README.md). This summary
records the evidence available on October 30; it does not certify later source
changes without corresponding verification.
