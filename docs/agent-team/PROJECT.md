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

# Project configuration — Keyloop Scenario A: Unified Service Scheduler

## Current scope decision — 2026-10-27

### Reviewer Docker setup — 2026-10-28

The user additionally authorized a Docker reviewer path that does not require an
existing local database. Root compose.yaml supplies PostgreSQL17.2 on a private
Compose network (no host5432 binding), a persistent volume, non-superuser app role,
explicit migration/seed services and the .NET8 API. This is an alternative to the
host-based instructions below. Production uses external JWT authentication;
reviewer testing supports the Development-only issuer described above. Repeated
starts preserve data and reuse valid fixtures; expired fixtures append a new set.
See [reviewer setup verification](runs/reviewer-simple-setup/verification.md)
for startup, authentication, restart persistence and migration-guard evidence.

### User-selected backend architecture (latest decision)

Read `docs/backend-architecture.md` as the current architecture/testing contract:
DDD, Clean Architecture, CQRS, in-memory event bus, .NET 8, EF Core 8/Npgsql,
JWT validation + RBAC, validation/middleware/errors/logging and PostgreSQL ACID.
External identity provider issuance is confirmed; local login/token issuance is
out of scope. Customer/Admin roles and single-organization scope are documented
assumptions. This supersedes old anonymous-demo identity and direct service patterns.
Keep existing ports/project layout and provider constraints. Backend implementation is authorized; see runs/backend-architecture-implementation/ for current evidence. Authentication deliberately changes access contracts.

The original PDF has now been inspected. Page 3 requires one fully implemented
layer, not both. Retain the BACKEND selection: REST API and persistent database.
Use existing OpenAPI/cURL examples as the minimal client substitute. React and
the custom .NET simulator are optional supporting tools; preserve existing code,
but do not require their implementation or suites to complete this assignment.

`specs/feature.md` defines current acceptance. This decision supersedes the older
mandatory frontend/simulator stages below. Section 7, UI work in section 9,
frontend commands, simulator reports and corresponding stages in section 10 are
reference material for optional tools, not required deliverables. Existing backend
contracts and safety rules remain applicable. Observability belongs in the design;
a particular telemetry implementation is a project choice.

The PDF does not prescribe framework/database versions, 20 concurrent clients,
fixture namespaces, business hours or timezone. Retain the technical choices below
as project constraints/assumptions, not assessment requirements. The historical
PostgreSQL 17.2 versus 17.4 issue is a project environment decision; do not silently
change it or describe 17.2 as required by the PDF.

For new work use ORCHESTRATOR.md's scope gate: inspect the existing implementation,
fix only backend gaps, verify core behavior against PostgreSQL, review independently,
then prepare design/README/tests/video evidence. Do not scaffold the app again.

Documentation updated: 2026-10-30.
This file defines implementation constraints for the Codex agent team working on
the existing application. It is not evidence that commands have been executed;
see [the verification summary](../verification.md) for recorded results and limits.

## 1. Scope and source of truth

Source: `KeyloopCodingChallange.pdf`, Scenario A (pages 1–2), challenge structure
(page 3), deliverables and evaluation (page 4).

The assessment requires:
1. Request a service appointment for a specific vehicle, service type, dealership
   and desired time.
2. Before confirmation, check BOTH a ServiceBay and a qualified Technician are
   available for the ENTIRE service duration.
3. Persist a successful Appointment associating customer, vehicle, technician
   and service bay.

The assessment allows one service layer to be fully implemented and the other
mocked/stubbed. **Selected approach: implement the backend fully.** OpenAPI/cURL
is sufficient as the client substitute. Existing React and .NET simulator clients
may support demonstrations but are optional for assessment completion.
There are no required external APIs in Scenario A; do not import Scenario D's
mock Sales/Service API requirements.

User-selected stack is mandatory: .NET 8, React 19.3, PostgreSQL 17.2 on local
port 5432. Other choices below are documented project assumptions, not PDF mandates.
Preserve an existing repository's structure where feasible and update paths here
if adopted paths differ. Do not silently upgrade or substitute the requested stack.

## 2. Stack and proposed repository layout

| Item | Configuration |
| --- | --- |
| Product | Keyloop Unified Service Scheduler |
| Solution | `Keyloop.ServiceScheduler.sln` at repository root |
| Backend | ASP.NET Core 8 REST API; target `net8.0`; controller-based endpoints |
| API project | `src/backend/Scheduler.Api/Scheduler.Api.csproj` |
| Domain | `src/backend/Scheduler.Domain/` — entities and booking rules |
| Application | `src/backend/Scheduler.Application/` — use cases and interfaces |
| Infrastructure | `src/backend/Scheduler.Infrastructure/` — EF mappings, migrations, persistence |
| Frontend | React and react-dom **19.3.0**, TypeScript SPA with Vite; `src/frontend/` |
| Frontend packages | npm; commit `package-lock.json`; pin matching React versions |
| JS runtime | Node 22 LTS; verify selected Vite's minimum Node version and pin a compatible patch in `.nvmrc` |
| Database | Existing local PostgreSQL **17.2**, `localhost:5432` |
| Demo database | `keyloop_scheduler_demo` |
| Test database | Separate disposable `keyloop_scheduler_test_<run-id>`; never reuse demo data for integration-test cleanup |
| EF provider | EF Core 8.x + Npgsql.EntityFrameworkCore.PostgreSQL 8.x; pin compatible exact patches |
| SDK/tools | Pin an installed .NET 8 SDK in `global.json` and dotnet-ef 8.x in a local tool manifest |
| API URL | `http://localhost:5080` (local development only) |
| UI URL | `http://localhost:5173` |
| Simulator | .NET 8 Generic Host console application, finite execution; `src/tools/Scheduler.Simulator/` |
| Simulator network | Calls API on 5080; no listening port and no direct database writes |
| Unit tests | xUnit; `tests/Scheduler.UnitTests/` |
| Integration tests | xUnit + WebApplicationFactory + real PostgreSQL 17.2; `tests/Scheduler.IntegrationTests/` |
| UI tests | Vitest + React Testing Library; optional Playwright browser smoke test |
| API contract | OpenAPI/Swagger generated by an ASP.NET Core 8-compatible package |

Dependencies: Domain has no infrastructure dependencies; Application depends on
Domain; Infrastructure implements Application interfaces; API composes them.
Simulator and frontend communicate through HTTP only. Prefer direct application
services over extra frameworks or generic repositories unless clearly justified.
The simulator may share DTOs, but must not reuse booking logic to calculate expected
results. No microservices, message broker or distributed cache in the first version.

## 3. Assumptions and business rules

Document these assumptions in `docs/system-design.md` as well:

- Authentication is user-requested beyond the PDF: validate external JWTs, enforce
  Customer/Admin policies and trusted issuer/subject-to-customer ownership mapping.
  CustomerId is input, never proof of identity. Use the architecture role matrix.
- A service type has a positive integer DurationMinutes determined by the server;
  never trust a client-provided end time or duration.
- One appointment needs exactly one qualified technician and one service bay.
  Both belong to the selected dealership. Qualification is an explicit
  TechnicianServiceType relationship, not an inferred name match.
- Service bays are interchangeable within a dealership for version 1. Specialized
  bay capabilities, multi-technician work and spare parts are out of scope.
- Service intervals use `[start, end)` semantics: an appointment ending at 10:00
  does not conflict with one beginning at 10:00. Any positive overlap conflicts.
- API timestamps must include an offset (`Z` or `+07:00`). Normalize to UTC and
  store PostgreSQL `timestamptz`. Display dealership-local time with a visible zone.
- Demo dealerships use `Asia/Ho_Chi_Minh`; business hours are 08:00–17:00 daily.
  Seed explicit dated availability windows for both resources for the next 14 days.
  The complete appointment must fit inside BOTH resource windows and opening hours.
  No holidays/breaks in the first fixture set; use explicit windows for negative tests.
- Reject start times in the past using injected `TimeProvider`. Tests use controlled
  clocks. UI must convert the dealership-local selection into an offset timestamp.
- Confirmed appointments are the only persisted appointment state in version 1.
  No cancellation, rescheduling, payments, reminders or waitlist workflows.
- Availability GET is advisory and never reserves resources. POST revalidates and
  commits atomically; another booking may win after a successful availability GET.
- Choose resource pairs in deterministic ID order. If a candidate pair loses a
  concurrent race, try other eligible pairs using a fresh transaction per attempt.
  A simple non-overlap guarantee must not wrongly reject usable alternative capacity.

## 4. Data model and conflict prevention

Required entities: Customer, Vehicle(CustomerId), Dealership(TimeZoneId),
ServiceType(DurationMinutes), ServiceBay(DealershipId), Technician(DealershipId),
TechnicianServiceType, ServiceBayAvailability, TechnicianAvailability, Appointment.

Appointment stores Id, CustomerId, VehicleId, DealershipId, ServiceTypeId,
TechnicianId, ServiceBayId, StartsAtUtc, EndsAtUtc, CreatedAtUtc. Persist the computed
end at booking time so later service-duration changes do not move existing bookings.
Use required foreign keys, positive duration checks and useful lookup indexes.
Validate ownership, qualifications and dealership consistency in the booking use case;
prefer composite FK constraints for same-dealership relationships where practical.

**A read-then-insert check alone is insufficient.** Enforce two PostgreSQL exclusion
constraints: one per technician/time range and one per bay/time range. Install
`btree_gist` through an EF migration. The intended SQL shape is below; use the
actual schema/table/column naming convention in the migration:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
ALTER TABLE appointments
  ADD CONSTRAINT appointment_positive_duration CHECK (ends_at_utc > starts_at_utc),
  ADD CONSTRAINT appointment_technician_no_overlap
    EXCLUDE USING gist
      (technician_id WITH =, tstzrange(starts_at_utc, ends_at_utc, '[)') WITH &&),
  ADD CONSTRAINT appointment_bay_no_overlap
    EXCLUDE USING gist
      (service_bay_id WITH =, tstzrange(starts_at_utc, ends_at_utc, '[)') WITH &&);
```

All resource IDs and interval columns must be NOT NULL. Both constraints apply to
all appointments because version 1 has only confirmed records. If cancellation is
added later, revisit predicates and tests explicitly.

Perform authoritative eligibility checks and insertion within a transaction. Resource
schedules/qualifications are read-only after seeding in version 1; future admin edits
need a consistency strategy. Catch only the expected resource exclusion violation
(SQLSTATE 23P01 with the known constraint name), roll back, then re-evaluate remaining
pairs in a bounded retry loop. Return 409 if no capacity remains; unexpected database
errors must not be disguised as a booking conflict. No in-process lock as the sole
protection. Tests must prove behavior across separate requests/DbContexts.

## 5. API contract to implement

All routes are under `/api` except health endpoints. Use GUID IDs and camelCase JSON.

| Method / route | Purpose |
| --- | --- |
| GET `/health/live` | Process liveness |
| GET `/health/ready` | Database connectivity and expected schema readiness; 503 if unavailable |
| GET `/api/dealerships` | Select a dealership |
| GET `/api/service-types` | Service names and server-owned durations |
| GET `/api/customers` | Admin-only customer lookup |
| GET `/api/customers/{id}/vehicles` | Vehicles belonging to selected demo customer |
| GET `/api/availability?dealershipId=...&serviceTypeId=...&startsAt=...` | Whether both resources are free for the full derived interval; return `available`, `startsAt`, `endsAt` and a reason when unavailable |
| POST `/api/appointments` | Atomically validate, allocate and persist; 201 + Location + appointment DTO |
| GET `/api/appointments/{id}` | Retrieve persisted booking, with all associated IDs and interval |
| GET `/api/appointments?customerId=...` | Authorized customer scope; deterministic order |
| GET `/api/demo/fixtures` | Development-only fixture IDs, scenario intervals, run namespace and seed timestamp |

POST body: `{ customerId, vehicleId, dealershipId, serviceTypeId, startsAt }`.
Apply the role/ownership matrix in `docs/backend-architecture.md` to every route.
Customer input must match verified identity; Admin may select an existing customer.
Add 401/403 and concealed-record 404 behavior as defined there. Earlier 400 ownership
errors below apply to Admin vehicle/customer relationship validation, not identity
spoofing. Customer booking uses owner-scoped vehicle lookup: foreign and unknown
vehicles both return equivalent 404 errors, as specified in backend-architecture.md.
Do not accept caller-selected technician/bay or end time in version 1.
Return 400 for invalid input/past time/Admin vehicle ownership mismatch, 404 for unknown
references, 409 for unavailable capacity, and ProblemDetails for errors. Availability
GET with valid inputs but no capacity returns 200 with `available: false`.
The backend is authoritative regardless of UI state. No external availability service.
CORS permits only the configured local UI origin; browser requests never access DB.

## 6. Local configuration

Use the already installed PostgreSQL on 5432. Do not launch another PostgreSQL
container on that port. Verify `SELECT version()` and stop to report a version
mismatch rather than silently using another server. Provision an empty demo database
and a non-superuser application role through the user's normal local admin workflow.
Migration setup may need permission for `btree_gist`; report permission failures.

Configuration contract for the API (values shown are templates, not credentials):

```json
{
  "ConnectionStrings": {
    "SchedulerDb": "Host=localhost;Port=5432;Database=keyloop_scheduler_demo;Username=<local-user>;Password=<local-password>"
  },
  "Cors": { "AllowedOrigins": ["http://localhost:5173"] },
  "Demo": { "Enabled": true, "SeedDays": 14, "TimeZoneId": "Asia/Ho_Chi_Minh" }
}
```

Store the connection string in .NET user-secrets or `ConnectionStrings__SchedulerDb`,
not committed JSON. Check secret existence without echoing it. .NET does not load
`.env` automatically: use shell environment variables/user-secrets or explicitly
implement and document a loader. Frontend `.env.local` contains only
`VITE_API_BASE_URL=http://localhost:5080` (public, not secret). `Demo.Enabled` defaults
to false outside Development, and fixture endpoints must be unavailable there.

Keep the app on the host initially. `localhost` in an application container is not
the host DB; containerizing the API would require an explicitly documented host name.

## 7. Optional simulator reference — not an assessment prerequisite

Interpretation: a **finite .NET 8 HTTP client test harness**, hosted with Generic Host,
that simulates customers booking through the REAL API against the REAL PostgreSQL
DB. It is not a mock of the scheduling API and does not replace the assessed logic.
It runs on demand, writes a summary and exits; no Windows service installation or
background loop required. React can be added after this harness proves core behavior.

Implement configuration binding using standard .NET command-line/environment providers:

```json
{
  "Simulator": {
    "BaseUrl": "http://localhost:5080",
    "Suite": "smoke",
    "Concurrency": 20,
    "RequestTimeoutSeconds": 10,
    "ReadyTimeoutSeconds": 30,
    "ReportPath": "artifacts/simulator/latest.json"
  }
}
```

The Generic Host entry point must await the suite and propagate failures to the
process exit code; do not swallow BackgroundService exceptions and report success.
Support `smoke`, `conflict` and `all` suite names. RequestTimeout applies to individual
requests; readiness polling has a total deadline. Run suites sequentially except the
intentional race test. Use a start barrier for concurrent POST requests, distinct
customers/vehicles and one predetermined contested interval. Do not retry POST
requests automatically; uncertain outcomes are test failures requiring investigation.

### Fixture creation

Implement API executable command mode `--seed-demo`: apply no hidden destructive
resets; seed fixtures and exit without starting the HTTP server. Require Development,
Demo.Enabled and exactly the allowlisted demo database name. Migrations are a separate
explicit command. Create a fresh fixture namespace per run with synthetic customers,
vehicles, dealerships, resources, qualifications and named future intervals. Return
that namespace via the development-only fixtures endpoint; reuse it throughout a suite.
Do not delete earlier appointments or other user data. Re-running seed creates a new
isolated namespace. Store fixture metadata persistently so API restarts do not lose it.

At minimum seed: a normal dealership with two qualified technicians and two bays;
an isolated race dealership with exactly one qualified technician and one bay;
separate scenarios for no qualified technician, occupied bay, occupied technician,
short availability window and adjacent time ranges. Include 30- and 60-minute services.
Each scenario owns different resources or intervals so preceding tests cannot alter
its expected outcome. At least 20 customers with matching vehicles for the race suite.
Use the injected clock to choose future dated fixtures within the 14-day windows;
fail clearly if fixtures have expired and request re-seeding. Do not hardcode dates.

### Required simulator assertions

| Suite | Scenario | Expected evidence |
| --- | --- | --- |
| smoke | Healthy API/schema + fixture load | Ready within deadline; complete fixture metadata |
| smoke | Valid request | 201; returned end equals service duration; both allocated resources present |
| smoke | Read back created appointment | GET returns matching persisted associations and interval |
| smoke | Unknown vehicle | 404; no appointment created |
| smoke | Vehicle/customer mismatch | 400; no appointment created |
| smoke | Past start | 400 |
| smoke | No qualified technician | Availability false; booking 409 |
| smoke | Bay occupied during part of service | 409 even if free at start |
| smoke | Technician occupied during part of service | 409 even if free at start |
| smoke | Resource window shorter than entire service | 409 |
| smoke | Back-to-back bookings on same resources | Both 201 with exactly touching boundaries |
| conflict | 20 distinct simultaneous requests, 1 eligible bay + 1 technician | Exactly one 201, nineteen 409, no 500/timeouts; exactly one stored appointment for contested resources/interval |
| conflict | 2 independent eligible pairs at one interval | Two 201; subsequent competing request 409 |

Report each scenario's inputs (synthetic IDs), expected/actual results, elapsed time,
HTTP statuses, created IDs, fixture namespace and overall result as JSON plus concise
console output. Exit 0 only if every selected assertion passes; nonzero for failed
assertions, dependency failures or skipped required checks. Use a nonzero distinct code
for unavailable prerequisites if helpful. No simulator logic writes to DB directly.
Persisted readback plus integration tests verify that storage is real; an optional
manual API restart followed by GET demonstrates persistence in the video.

## 8. Required commands and scripts to implement

The following are TARGET commands for the code to be generated. They do not exist
just because this file exists. Verify scaffolding, project names and package scripts
before claiming successful execution. Working directory is repository root unless noted.

| Action | Command |
| --- | --- |
| Restore tools/packages | `dotnet tool restore` then `dotnet restore Keyloop.ServiceScheduler.sln` |
| Backend build | `dotnet build Keyloop.ServiceScheduler.sln --no-restore` |
| Apply migrations | `dotnet ef database update --project src/backend/Scheduler.Infrastructure --startup-project src/backend/Scheduler.Api` |
| Seed fresh demo fixtures | `dotnet run --project src/backend/Scheduler.Api --no-launch-profile -- --seed-demo` |
| Start API | `dotnet run --project src/backend/Scheduler.Api --no-launch-profile -- --urls http://localhost:5080` |
| Unit tests | `dotnet test tests/Scheduler.UnitTests` |
| Integration tests | `dotnet test tests/Scheduler.IntegrationTests` |
| Simulator smoke | `dotnet run --project src/tools/Scheduler.Simulator -- --Simulator:Suite=smoke` |
| Simulator race | `dotnet run --project src/tools/Scheduler.Simulator -- --Simulator:Suite=conflict --Simulator:Concurrency=20` |
| Simulator all | `dotnet run --project src/tools/Scheduler.Simulator -- --Simulator:Suite=all` |
| Frontend initial install | In `src/frontend`: `npm install` once to generate the committed lockfile |
| Frontend reproducible install | In `src/frontend`: `npm ci` after lockfile exists |
| UI startup | In `src/frontend`: `npm run dev -- --host localhost --port 5173 --strictPort` |
| UI checks | In `src/frontend`: `npm run lint`, `npm run test -- --run`, `npm run build` |

For the local PowerShell API/migration terminal set
`$env:ASPNETCORE_ENVIRONMENT = 'Development'` and `$env:Demo__Enabled = 'true'`.
Configure connection strings privately before migration/seed. Do not put passwords in
README examples or terminal transcripts. Seed and API startup must use the same
connection configuration. Leave the API running in one terminal and run the simulator
in another. Re-seed before each independent suite run, including repeated `all` runs.

Integration tests use `ConnectionStrings__SchedulerTestDb` supplied privately. Either
create a disposable test DB on the local 17.2 server with explicit permission, or
provide a dedicated test DB in advance. Never fall back to demo or production storage.
Run migrations and deterministic fixtures for the test database; clean only owned test
resources. A Testcontainers alternative may use `postgres:17.2` with a dynamically
mapped port to avoid competing with local port 5432; it is optional, not the first-run
prerequisite. Confirm provider-specific tests really executed rather than using EF
InMemory/SQLite substitutes. Missing DB access means blocked verification.

## 9. UI scope, observability and quality

Basic React UI: choose customer, vehicle, dealership, service and desired local time;
show server-derived duration and availability; submit; display allocated resources,
confirmation and saved appointments. Include loading/error/empty states and a clear
409 message after a race. Disable repeated in-flight submission but keep server
concurrency protection. Use accessible labels and a visible dealership time zone.

Observability: structured ILogger events with request/trace ID, service/dealership IDs,
booking result and elapsed time. Avoid names, VINs, credentials and full payloads in
logs. Add .NET Meter counters for confirmed/conflicting/failed bookings and a duration
histogram; ActivitySource spans around booking and persistence. Console output and
local diagnostics suffice initially; no mandatory telemetry platform. Document how
to inspect logs/metrics/traces, even if an OTLP collector is deferred.

Test business duration, eligibility, full-interval overlap, boundaries, invalid input
and concurrent allocation using actual PostgreSQL. Add an API persistence test across
fresh contexts/hosts and a health-failure check. Measure simulator elapsed times as
evidence, not a fabricated performance SLA. Use bounded queries and suitable indexes.
CI must build/test the backend, execute real-provider integration tests and check the
frontend if included. Document environment gates and any blocked tests explicitly.

## 10. Agent execution plan and deliverables

1. Analyst: map the three PDF requirements to acceptance IDs; keep design assumptions
   distinct from requirements. Record the backend-layer choice.
2. Planner: finalize API/data contracts and file ownership. One owner for migrations,
   shared DTOs and lockfiles. Record the seed/fixture schema before simulator coding.
3. Developer: scaffold solution, persistence, exclusion constraints, API, seed command,
   health checks and unit/integration tests. Establish a passing booking vertical slice.
4. Optional developer task: maintain the existing finite simulator and reports.
   It can proceed against the agreed contract, but end-to-end completion waits for API.
5. Tester: reuse existing provider tests for booking/persistence and add the architecture,
   JWT/ownership and event tests required by the current spec. Existing tests alone
   do not satisfy new ARCH/SEC/EVT criteria; simulator suites are optional evidence.
6. Optional developer task: maintain React only when explicitly in scope.
7. Constraint reviewer: verify full-duration checks, qualification, persistent resource
   associations, race protection and exact stack. Simplifier: only bounded cleanup.
8. Orchestrator: rerun affected checks and produce the verification report. Never
   report a target command or proposed test as an actual completed run.

Required submission artifacts:
- `docs/system-design.md`: architecture diagram, component roles, data flow, stack
  justifications, assumptions, scalability/performance/reliability/maintainability,
  observability strategy and dedicated GenAI design-assistance section.
- Working Git repository with backend source, migrations, tests and `README.md`:
  installation, build/run/test/seed steps, environment configuration and API examples.
- README AI Collaboration Narrative: actual prompts/strategy, mistakes found, human
  decisions, verification/refinement and ownership. Do not invent an AI history.
- `docs/verification.md`: acceptance mapping, executed commands, results and gaps.

Out of scope: Scenarios B–D, production identity provider, payments, deployment,
cancellation, stock integration and external dealership APIs. Future design discussion
may mention these, but do not implement them unless explicitly requested.

## 11. Technical references

- React version confirmation: https://react.dev/versions (19.3.0 listed).
- Npgsql EF Core 8 provider: https://www.npgsql.org/efcore/release-notes/8.0.html
- PostgreSQL 17 range/exclusion constraints:
  https://www.postgresql.org/docs/17/rangetypes.html
- PostgreSQL 17 btree_gist:
  https://www.postgresql.org/docs/17/btree-gist.html

Version checks here describe the intended environment; no local PostgreSQL, Node,
.NET build, simulator or application execution was performed while editing this file.

Implementation verification supports the documented optional isolated container on an explicitly configured localhost test port (SchedulerTests__PostgresPort, default5432). This does not change the PostgreSQL17.2 target or demo/application ports. Current run uses55432 and a separately named test database.
