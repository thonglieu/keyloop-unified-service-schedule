> Setup revision (2026-10-29): reviewer setup includes an explicitly enabled
> Development-only local signed-JWT issuer for fixed synthetic Admin/Customer
> accounts and generated database credentials. This supersedes the earlier blanket
> exclusion of local token issuance for reviewer testing only. Production retains
> external JWT authentication. Evidence: docs/agent-team/runs/reviewer-simple-setup/.

> Current decision (2026-10-29): the user explicitly requested removal of the
> frontend and simulator implementation. This supersedes earlier instructions to
> preserve or maintain those clients. The selected layer is backend; Swagger/OpenAPI
> and docs/api-demo.http provide the Part 2 client substitute. README.md is the
> reviewer guide; AI-Usage.md preserves AI collaboration and workflow history.
> Do not add orchestration instructions or historical setup paths to the README.

# Scenario A — Unified Service Scheduler

Status: backend architecture implemented and locally verified; see the implementation run for executed evidence and the intermittent telemetry caveat. Final submission video remains outstanding.
Updated: 2026-10-27. Selected implementation layer: **backend**.
Source: `KeyloopCodingChallange.pdf`, pages 1-2 (Scenario A), page 3
(design and layer choice), page 4 (submission and evaluation).
Configuration: `docs/agent-team/PROJECT.md`. Workflow: `docs/agent-team/ORCHESTRATOR.md`.

## Assignment scope

Implement ONE layer fully. For the selected backend, expose a RESTful API and use
a persistent database. Substitute cURL examples, OpenAPI or a simple test harness
for the client. A complete React app and a custom simulator are not required.
The frontend alternative would implement the full user experience with a mocked
backend; it is not the selected track in this repository.

| ID | Explicit requirement | Observable backend acceptance |
| --- | --- | --- |
| A-01 | Request service for a vehicle, service type, dealership and desired time | REST request accepts these inputs and identifies the customer |
| A-02 | Check both a ServiceBay and qualified Technician for the entire duration before confirming | Confirm only if both cover the whole derived interval; otherwise reject without saving |
| A-03 | Persist Appointment associating customer, vehicle, technician and bay | Save those associations in the database and demonstrate retrieval through a fresh context or restarted API |

## Correctness checks and retained assumptions

The user-selected backend architecture is defined in
[`docs/backend-architecture.md`](../docs/backend-architecture.md). It extends the
assignment and supersedes previous demo-only/no-authentication assumptions.
External identity provider JWT issuance is confirmed; API validates tokens and
enforces RBAC plus ownership. Customer/Admin are the initial role assumption.

| ID | User-selected architecture acceptance |
| --- | --- |
| ARCH-01 | DDD Appointment aggregate/value objects protect invariants; Domain has no EF/HTTP dependencies |
| ARCH-02 | Clean dependencies; controllers call Application handlers, not DbContext; provider details stay in Infrastructure |
| ARCH-03 | Separate CQRS command/query handlers over one PostgreSQL DB; reads never reserve or write |
| SEC-01 | Real JWT validation rejects missing, expired, malformed, wrong issuer/audience/signature tokens; role policies return 401/403 appropriately |
| SEC-02 | Verified issuer/subject maps to customer; own-record filters and object checks prevent customerId/appointment-ID spoofing; test Admin permissions too |
| EVT-01 | In-memory event dispatch occurs only after commit; failed/retried attempts publish nothing; subscriber failure cannot reverse or misreport a committed booking |
| DATA-01 | PostgreSQL ACID transaction, constraints, fresh-attempt retries and independent readback verified with real-provider tests |
| API-01 | Layered validation, ordered middleware, sanitized ProblemDetails and correlated structured logs; no tokens/secrets/PII in logs |
| TEST-01 | Architecture/domain/application, real JWT HTTP, real PostgreSQL/concurrency and event-failure checks execute with recorded results; missing prerequisites remain blocked |

These support the requirements; the PDF does not dictate their precise implementation.
Preserve existing public contracts and stack. Do not add packages or architecture
merely to narrow the assessed scope.

| ID | Evidence needed for this backend implementation |
| --- | --- |
| Q-01 | Server derives duration; validate references and vehicle ownership; reject invalid or past inputs without writes |
| Q-02 | Test missing/unqualified technician, missing bay, partial overlaps and insufficient availability windows independently |
| Q-03 | Test half-open boundaries: adjacent appointments may succeed; positive overlaps may not |
| Q-04 | Revalidate at booking time; concurrent requests cannot double-book either resource; usable alternative capacity remains usable |
| Q-05 | Real PostgreSQL tests prove persistence, independent bay/technician exclusions and atomic failure behavior; mocks alone are insufficient |
| Q-06 | Document timezone, opening hours, one technician/one bay and immutable confirmed intervals; update error contracts explicitly for JWT/ownership |

Existing assumptions include server-owned positive durations, UTC timestamps,
`[start,end)` intervals, Asia/Ho_Chi_Minh demo times, 08:00-17:00 opening hours,
seeded resource windows. JWT identity replaces caller-selected demo identity. Existing PostgreSQL
exclusion constraints remain the chosen concurrency mechanism. The PDF does not
require their SQL syntax, exactly 20 contenders, or an exact database patch version.
Preserve prior project version constraints pending an explicit compatibility decision;
report environment limitations separately from assessment functionality.

## Submission acceptance

| ID | Required artifact and evidence |
| --- | --- |
| D-01 | System design: architecture diagram, component roles, data flow, technology justifications, observability strategy and dedicated GenAI design-assistance section |
| D-02 | Working Git repository with backend, persistent database setup and tests of core business logic |
| D-03 | README with reproducible build/run/test instructions and AI Collaboration Narrative describing actual direction, verification, refinement and ownership |
| D-04 | Client substitute: cURL examples, OpenAPI contract or simple HTTP harness sufficient to demonstrate the API |
| D-05 | Recorded 5-10 minute video: introduction, design/implementation highlights, 1-2 minute AI story, application demonstration, lessons and challenges |

Consider scalability, performance, reliability, maintainability and observability
in design and implementation. No distributed deployment or telemetry platform is
mandated. A script does not satisfy D-05; the user records the final video.

## Execution and completion

1. Inspect source and historical evidence; map all A/Q/D and architecture/security/event/data/API/test IDs to behavior and gaps.
2. Plan only remaining backend fixes and verification with bounded file ownership.
3. Delegate necessary fixes to developer, real database checks to tester, and
   independent requirement/correctness review to constraint_reviewer.
4. Demonstrate persisted success and resource rejection through OpenAPI/HTTP.
   The existing simulator may be reused if useful.
5. Finish submission documentation and record actual evidence and remaining gaps.

Use `docs/agent-team/runs/backend-architecture-definition/` for this design phase;
the earlier scope correction is in `docs/agent-team/runs/scenario-a-scope-correction/`.
Future implementation uses a separate run. Historical AC-01 through AC-27 reports describe
the expanded scope and are not fresh test evidence. Do not overwrite old results or
infer backend verification from source inspection, mocked UI tests or readiness 200.

React, simulator suites, rich fixture reporting and UI polish are optional. Keep
existing implementations; do not delete/refactor them to reduce scope. If changed
later, run affected checks. Optional tools are not backend acceptance gates.
Scenarios B-D, external dealership APIs, payments, cancellation/rescheduling,
local identity-provider implementation, account registration/password management
and deployment remain outside this task. JWT validation/RBAC are now in scope.
