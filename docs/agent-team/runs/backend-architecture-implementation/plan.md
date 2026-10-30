# Implementation plan

IMPL-ANALYZE: spec_analyst, read-only gap audit.
IMPL-PLAN: planner, read-only tasks/contracts/ownership.
IMPL-CORE: developer, sequential backend implementation; sole shared-contract, migration and API-composition owner. Await baseline and plan before service edits.
IMPL-TEST: independent tester, tests-only ownership after developer handoff; real JWT and PostgreSQL evidence required.
IMPL-REVIEW: constraint_reviewer, read-only integrated review.
IMPL-SIMPLIFY: simplifier, justified cleanup only after correctness; no change is acceptable.
Main session owns shared run records, README, system design, HTTP instructions and integration.
Serialize builds/tests using shared outputs and databases. No production actions or frontend expansion.

IMPL-CORE assigned to Dewey: exclusive src/backend/** ownership; backend-only builds permitted; no database migrations or test/docs edits. Contract announcement precedes independent tester work. IMPL-ANALYZE completed read-only; no material business blockers.

## Planner-approved slices and acceptance

| Slice | Owner / scope | Acceptance and dependency |
| --- | --- | --- |
| CORE.1 | Dewey: Domain/Application | ARCH-01..03, A-01..03, Q-01..03/Q-06; factory/interval/events, handlers, narrow read/attempt/current-user ports |
| CORE.2 | Dewey: Infrastructure | DATA-01, SEC-02, Q-04..05; fresh contexts, Read Committed, unchanged exclusions, identity mapping migration, additive seed adaptation |
| CORE.3 | Dewey: API and integrations | SEC-01..02, API-01, ARCH-02..03, D-04; JWT config, claims, policy/ownership, ProblemDetails/logs, DTO queries, readiness, OpenAPI |
| CORE.4 | Dewey: Application/Infrastructure | EVT-01; after known commit/disposal, noncritical sequential cooperative dispatch |
| TEST | McClintock: tests/** only | All executable acceptance; signed JWT actors, domain/architecture, provider rollback/concurrency, events; test execution serialized with developer |
| REVIEW | constraint_reviewer, read-only | Integrated snapshot, independent security/ACID/event/contract review |
| SIMPLIFY | simplifier, initially read-only | Only justified changes after correctness; transfer ownership first; no-change valid |
| FINAL | Main: docs and integration | D-01..04 evidence and reproducible instructions; D-05 user recording remains separate |

Public HTTP fields/routes and deterministic ordering preserved. Admin precedence;
Customer foreign vehicle/record concealed 404; spoofed customer/filter 403. Required
appointment customerId filter preserved. Problem codes follow architecture contract.
No fake auth scheme in bearer tests; no in-memory DB substitute for provider tests.
Planner IMPL-PLAN completed read-only. Baseline and plan precede backend edits.

Main integration scope also owns .github/workflows/verify.yml: retain pinned PostgreSQL17.2 and existing frontend job; capture backend TRX and upload evidence on failure. CI execution is not claimed locally.
