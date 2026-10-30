# AI usage and collaboration

Documentation updated: **2026-10-30**.

This document records AI-assisted decisions, implementation, review and correction
for the backend submission. Use [README.md](README.md) for current setup and API
operation, and [verification](docs/verification.md) for recorded checks and limits.
This documentation update did not run builds or tests.

## Scope and workflow

The selected assessment layer is the backend: an ASP.NET Core REST API with
PostgreSQL persistence. Swagger/OpenAPI and [HTTP examples](docs/api-demo.http)
provide the demonstration client. The [feature specification](specs/feature.md)
and [backend architecture contract](docs/backend-architecture.md) define the
acceptance criteria and implementation boundaries.

The user authorized implementation after design, selected the
.NET 8/PostgreSQL/CQRS/security/event boundaries, and required independent roles.
The orchestrator coordinated analysis, planning, implementation, testing and
read-only review, with explicit file ownership and preservation of existing edits.
For future AI maintenance, follow [AGENTS.md](AGENTS.md),
[PROJECT.md](docs/agent-team/PROJECT.md) and
[ORCHESTRATOR.md](docs/agent-team/ORCHESTRATOR.md).

The working strategy was to define contracts and business invariants first, assign
bounded implementation tasks, exercise real HTTP and database behavior, return
failures to the responsible developer, and independently review the corrections.
The descriptions below summarize recorded work; they are not verbatim prompts.

## Backend implementation and review: October 27-28

Read-only analysis and planning constrained changes to missing backend behavior.
One developer owned shared contracts, migrations and API composition. An
independent tester added real JWT bearer, customer ownership, PostgreSQL
transaction/concurrency and event-failure checks. A separate reviewer inspected
architecture boundaries, authorization and transaction behavior.

Review and execution exposed concrete issues that required correction:

- Diagnostic callbacks and transaction cleanup could interfere with behavior after
  a booking had committed. Corrections preserved successful booking outcomes and
  made event/diagnostic handling conservative when cleanup was uncertain.
- The EF design-time factory did not honor the configured connection. Its
  configuration handling was corrected while preserving connection-free scaffolding.
- Empty identity-claim configuration and database exception classification produced
  incorrect HTTP responses. Regression checks covered the corrected behavior.
- Concurrent booking attempts exposed PostgreSQL deadlocks. At the user's request,
  GPT-6-Luna with medium reasoning implemented transaction-scoped resource locks
  and a fresh eligibility check after waiting. Independent exclusion constraints
  remained in place. Provider tests and repeated capacity races verified the fix.

The local PostgreSQL server was 17.4 while the selected test target was 17.2.
The work used an isolated PostgreSQL 17.2 container on a separate loopback port,
with a dedicated test database and non-superuser test role. Test JWT keys were
synthetic; the tests did not depend on a live external identity provider.

The final October 28 report records 28/28 unit tests, 140/140 integration tests,
and three repeated concurrency runs with 2/2 cases passing each time. It also
records a vulnerability-feed warning and an intermittent telemetry observation;
later results are described below. Narrow simplification review found no
justified additional changes.

See [backend implementation evidence](docs/agent-team/runs/backend-architecture-implementation/verification.md)
for executed commands, findings and limitations. Earlier failed attempts in that
report are not successful verification of the final implementation.

## Reviewer setup and documentation: October 29

The user requested a reviewer-focused README. Operational instructions were
separated from the AI collaboration narrative, and independent review checked
architecture, authorization, resource locks, events and metrics against source.
Review corrected the TCP readiness probe and the documentation of the Admin-only
customer route.

Docker setup work covered database initialization, a restricted application role,
explicit migrations, demo fixtures and persistent appointments. Reviewer startup
added a local authentication path for the demonstration environment. Recorded
HTTP smoke checks exercised issued reviewer accounts, Admin fixture access and
customer-owned vehicle access. Repeated startup preserved configuration and the
fixture namespace.

Testing exposed two additional issues:

- A full integration run observed an empty telemetry collection despite a correct
  HTTP 503 response. Focused success alone was not treated as resolving the full
  suite failure. The setup report records fault-injection callback cleanup,
  reproduction checks and a final 159/159 integration pass.
- The lifecycle failure-guard assertion was intercepted by reviewer authentication.
  Its test override was corrected, then the complete lifecycle check passed with
  exit code 0. The original guard assertions remained intact.

The final setup report records a clean solution build, 28 passing unit tests,
159 passing integration tests, successful startup and restart persistence, and
migration guard failure preventing API startup. Independent review found no
remaining blocking setup defects.

Supporting records:

- [Reviewer documentation review](docs/agent-team/runs/reviewer-documentation/verification.md)
- [Reviewer setup and Docker lifecycle evidence](docs/agent-team/runs/reviewer-simple-setup/verification.md)

These reports describe successive checks. The final setup report supersedes
pending statuses in the documentation review, including the telemetry limitation.
It records the final successful lifecycle run without treating earlier failed
attempts as successful checks.

## Docker test verification: October 30

The standalone Docker test workflow built the test image and ran against a fresh
temporary PostgreSQL 17.2 database. The command exited 0 with **28/28 unit tests**
and **159/159 integration tests** passing: **187 total**, zero failures or skips.
Build layers were cached. Exported TRX results remained available after test
containers and the network were removed.

Independent review covered Compose configuration, the image, test runner,
localhost guard, source layout and subprocess tests. No blocking defects were
recorded. Runner failure propagation was inspected in source; deliberate failure
injection into the runner was not executed.

See [Docker test evidence](docs/agent-team/runs/reviewer-compose-tests/verification.md)
and the [verification summary](docs/verification.md). These are existing recorded
results, not tests rerun for this document update.

## Human ownership and remaining limits

The user owns scope decisions, acceptance of the implementation, real deployment
configuration and the final demonstration video. AI-generated code and documents
were checked through independent review and executed tests; generated examples
alone are not evidence that a scenario ran.

Remote GitHub Actions execution, a live external identity provider, production
deployment/migrations, throughput benchmarks and power-loss durability tests are
not established by the recorded local checks. In-memory events remain non-durable,
and allocation locks serialize attempts sharing resources even for disjoint
intervals. Later source changes require their own verification.
