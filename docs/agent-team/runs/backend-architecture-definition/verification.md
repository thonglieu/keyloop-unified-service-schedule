# Verification

Read existing source/docs through parent and independent spec_analyst. Consulted
official Microsoft JWT/EF transaction and PostgreSQL isolation references, linked
in architecture document. Target requirements are distinct from existing behavior.
Independent constraint review found no blocking issues. Two minor wording issues
(additional test coverage and current run path) were corrected. `git diff --check`
passed with line-ending notices only. Earlier scope-correction edits are preserved;
no service changes are attributed to this documentation phase.

No build, tests, migration, database mutation or service changes in this run.
Previous runtime evidence is historical; new architecture acceptance is unverified.

## Current pass: acceptance and test plan

All entries below are planned checks, not executed results. B means
tests/Scheduler.IntegrationTests/BookingTests.cs; U means
tests/Scheduler.UnitTests/BookingRulesTests.cs; E means
tests/Scheduler.IntegrationTests/EnvironmentTests.cs. Existing AC numbers identify
source test names only. A and D are assignment requirements; Q are correctness
checks/retained assumptions; ARCH/SEC/EVT/DATA/API/TEST are user-selected constraints.

| ID | Existing source coverage | Required planned evidence / gap |
| --- | --- | --- |
| A-01 | B AC01_03_08_09 | Authenticated POST, 201/Location and matching requested associations |
| A-02 | B AC04_05_06, AC06 | Qualified technician AND bay cover full interval; add zero-technician case; rejected requests write nothing |
| A-03 | B AC01_03_08_09 | Preserve independent context and new-host readback with authorized identities |
| ARCH-01 | U helper rules only | Aggregate/value-object invariants, immutable confirmed interval, no Domain EF/HTTP package or assembly references |
| ARCH-02 | None | Project/assembly dependency checks and controller dependency inspection; no DbContext/provider use in controllers |
| ARCH-03 | B AC12 advisory check | All query handlers return DTOs and never save, reserve or emit events; same database for command/read paths |
| SEC-01 | None | Actual bearer handler validates signed Customer/Admin tokens; missing/malformed/expired/unsigned/wrong issuer/audience/key and ambiguous subject rejected; 401 challenge, wrong role 403; invalid configuration fails closed |
| SEC-02 | B anonymous vehicle mismatch only | Unique exact issuer/subject mapping; own filters; unmapped scoped operation 403; spoofed customer/filter 403; foreign record/vehicle 404 equivalent to missing; Admin and dual roles; direct-handler roles/ownership |
| EVT-01 | None | Subscriber independently reads committed row; no rollback/retry-loser dispatch; exception/resolution failure allows committed success; later handlers run within budget; cooperative timeout/disconnect and restart without replay |
| DATA-01 | B persistence/races/exclusions; E AC26 connection failure | Inject failure after SaveChanges before commit and assert rollback/no event; deterministic fresh-context retry; unknown commit not retried; Read Committed, fsync/synchronous_commit, restart readback |
| API-01 | B errors; E telemetry/readiness | Code/traceId/field errors; sanitized 400/401/403/404/409/500/503 and bearer headers; minimal health exception; captured structured logs exclude synthetic token/PII/secret canaries; middleware behavior |
| TEST-01 | Existing xUnit projects | Execute all relevant suites; record command/exit/discovery/execution/skip counts and provider prerequisites; missing coverage stays blocked |
| Q-01 | B AC01_02_14, AC02; U offsets | Server duration; invalid/past/unknown/ownership no-write checks; add overflow, malformed types, duration override and exact clock boundary; Admin handles unknown-customer cases |
| Q-02 | B resource/window cases | Add zero technicians, missing windows and foreign-dealership resources; independently test qualification, bay, each partial overlap and short window; adjacent windows do not combine |
| Q-03 | U adjacency; B AC06/07/13 | Both overlap directions, equal/contained intervals, adjacency and exact resource-window edges |
| Q-04 | B AC10_11, AC12 | Advisory GET race; start barrier across contexts; exactly available capacity; deterministic first-choice loss proves alternative retry branch |
| Q-05 | B AC13 independent exclusions | Real PostgreSQL SQLSTATE/constraint names, FKs/NOT NULL/positive interval and injected atomic failure; no substitute provider |
| Q-06 | U hours; B AC14/15 | UTC/offset equivalence, opening boundaries, one resource pair, fixed booked end after duration update; documented authorization error changes |
| D-01 | Design docs exist | Review final diagram/roles/flow/technology/observability/GenAI section against implementation; target and baseline remain distinct |
| D-02 | Existing source/tests | Reproduce backend build/setup and real-provider persistence after authorized implementation |
| D-03 | README exists | Reproduce instructions and verify AI narrative against actual work; no invented execution history |
| D-04 | HTTP tests/examples exist | Execute authenticated OpenAPI/cURL booking, saved GET and unavailable-resource rejection; no embedded secrets |
| D-05 | Script existed at the time of this run; presentation notes are now maintained outside the repository | User supplies actual 5-10 minute recording including 1-2 minute AI story; video unverified |

Test fixtures must gain synthetic signed JWT actors, not a fake authentication
scheme. Existing unknown-customer and vehicle relationship tests use Admin tokens;
Customer tests assert the new concealment contract. Keep isolated test DB guards
and controlled clocks. No testing was run: integration initialization applies
migrations and is outside this phase. PostgreSQL version/access are unprobed here.

Read-only checks performed: file/source/test inspection, rg discovery, git status,
baseline commit identification. PDF extraction attempts failed (no usable Python
installation or pdftotext); current spec and earlier analysis supplied the mapping.
Historical success statements above belong to the prior documentation pass.

Final documentation checks: acceptance-ID comparison found 23 unique spec IDs and zero missing matrix rows. git diff --check passed (line-ending notices only); git diff --name-only -- src tests returned no changes. Reviewer follow-up found no design blockers; duplicate clarification paragraphs were removed. No executable application checks ran.
