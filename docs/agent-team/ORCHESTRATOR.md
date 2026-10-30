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

# Orchestrator workflow

The primary Codex session owns this workflow. Do not spawn a second orchestrator.
This is an instruction-driven workflow, not a deterministic execution engine.

## Scenario A scope gate (2026-10-27)

Read `KeyloopCodingChallange.pdf` pages 1-4 and `specs/feature.md` before planning.
The PDF requires ONE fully implemented layer. This repository selects BACKEND:
REST API + persistent database; cURL examples, OpenAPI or a simple HTTP harness
stand in for the client. React and the custom simulator are optional supporting
tools, not completion prerequisites. Preserve their existing code.

Classify each task as assignment requirement, correctness deduction, project
constraint/assumption, or optional enhancement. Cite its source and acceptance ID.
Database patch versions, exact race sizes, seed namespaces and custom simulator
suites are not PDF mandates. Preserve existing stack constraints; report version
blockers separately rather than silently changing the environment.

Audit existing code and executed evidence first. Plan remaining gaps rather than
rebuilding the architecture. Default to lightweight mode: main-session analysis
and plan, bounded developer fixes when needed, tester for real-provider checks,
and independent constraint review. Never schedule a frontend phase automatically.
Use full mode only when complexity warrants it. For a requirements/workflow
correction, delegate read-only analysis and review; the main session owns docs.
Do not start application implementation just because the plan contains future tasks.

Backend completion requires current A/Q acceptance checks and D submission
artifacts in the spec. OpenAPI/cURL can satisfy the client substitute; React tests
cannot prove database persistence. Preserve historical AC-01 through AC-27 reports
as historical evidence; create a new run for the corrected scope. Missing provider
coverage remains blocked; optional UI failures alone do not block backend acceptance.

## Modes

### Backend architecture and test gates

Read `docs/backend-architecture.md` with PROJECT/spec before any service work.
The user's DDD/Clean/CQRS/event/JWT/RBAC/ACID requirements are now binding project
requirements beyond the PDF. Include ARCH/SEC/EVT/DATA/API/TEST acceptance IDs.
Architecture-definition tasks modify documentation only; do not start service
implementation until requested. The explicit 2026-10-27 implementation request now authorizes the staged workflow; START_HERE.txt records that phase.

For later implementation: baseline tests -> bounded Domain/Application CQRS and
persistence boundary work -> JWT/RBAC identity mapping -> validation/error pipeline
-> after-commit event bus -> integrated provider/security/event tests -> review.
Assign one owner each for migration/model changes, shared ports and API composition.
Preserve provider concurrency tests through the refactor. Tester must execute real
JWT bearer validation as well as real PostgreSQL tests; mocked claims alone are
insufficient. Reviewer checks inward dependencies, object authorization, ACID and
event failure semantics. Never claim the in-memory bus provides durable delivery.
Use disposable test databases and synthetic token keys. No secrets in reports.

Do not silently preserve anonymous access for optional clients; explicitly update
API documentation for the intentional authentication contract change. Production
identity infrastructure, client UI and durable message delivery are not implied.

- lightweight (default): do analysis/planning in the main session, delegate coding
  to developer, and delegate independent review to constraint_reviewer. Use tester
  when tests or executable acceptance checks warrant a separate assignment.
  Have developer perform any justified simplification after review.
- full: use spec_analyst, planner, developer, tester, constraint_reviewer and
  simplifier in the stages below. These are roles, not six simultaneous workers.
- review-only: analyze the requested diff and run relevant checks, without edits.

Limit active subagents to three. Wait for dependent work. Close completed threads
when supported to release slots. Reuse a role for follow-up fixes when possible.

## 0. Establish scope and baseline

Read the spec, AGENTS.md files, PROJECT.md, current Git status and relevant code.
Identify existing uncommitted changes; record the starting commit and relevant
pre-existing diff so review can distinguish feature edits from existing work.
Do not claim that the diff against HEAD is exclusively this feature if it is not.
Discover actual commands from the repository; do not invent npm scripts.

Create docs/agent-team/runs/<feature-slug>/ with four concise records:
- analysis.md: requirements, acceptance IDs, risks and assumptions.
- plan.md: tasks, dependencies, owners, write scopes and agreed contracts.
- status.md: current phase, completed/pending/blocked tasks and handoffs.
- verification.md: acceptance matrix, executed checks, findings and final result.

Only the orchestrator writes these shared records. Specialists return their
results to the parent. Never let several agents update a shared task file.

## 1. Analyze

In full mode, delegate to spec_analyst with the original spec and relevant paths.
Require observable acceptance criteria, missing business decisions, risks and
references to existing behavior. Resolve ordinary choices with repository patterns;
ask the user only when an unknown materially changes behavior or scope.
Record assumptions explicitly. Do not quietly turn assumptions into requirements.

## 2. Plan

In full mode, delegate to planner with the specification and accepted analysis.
Require file-level ownership, API/data contracts, dependencies and verification.
Freeze a minimal contract before separate frontend/backend implementation.
Prefer one developer for a small vertical slice. For genuinely independent work,
spawn two developer instances with distinct scopes and the same agreed contract.

## 3. Implement

Give each developer a task using TASK_TEMPLATE.md. One writer per file at a time.
Subagents are not automatically isolated: inspect the actual workspace model.
For parallel writers, prefer separate Git worktrees if supported, or strictly
disjoint paths. Do not assume a branch alone isolates a shared working directory.
Integration is owned by the main session. Preserve external/user changes.
Do not let agents independently edit shared DTOs, lockfiles, migrations or config.
Assign such files to one named owner. Reassign ownership only after its prior
writer has finished. No destructive Git operations or automatic pushes.

## 4. Verify and review

Stop application edits and identify the exact snapshot under review.
Give tester the original acceptance criteria, plan and relevant code, not just the
developer's success summary. Assign a test-only write scope if tests are needed.
Give constraint_reviewer the original spec, constraints, baseline and final diff.
These roles may run in parallel when resources are independent. Serialize checks
that share a database, browser profile, ports, build outputs or mutable fixtures.
If test additions change the review snapshot, review those additions too.

## 5. Fix and simplify

Triage findings by impact. Return fixes to the owning developer. Ask reviewers to
verify resolved findings. Cap automatic repair/review cycles at three; if the same
issue persists, summarize the cause and ask for a decision rather than loop forever.
In full mode, give simplifier a narrow write scope after correctness is established.
It may report no change warranted. Do not refactor unrelated code or remove tests.

## 6. Final verification and delivery

Run affected checks against the final integrated tree after all fixes/refactoring.
Compare every acceptance ID with evidence. Recheck changed security/contract paths.
Passing tests with unavailable database/browser coverage means partially verified,
not fully verified. Record pre-existing failures separately with baseline evidence.
Complete verification.md using VERIFICATION_TEMPLATE.md.
Report changed behavior, relevant files, commands/results, unresolved findings and
verification gaps. Include the final Git status if available.
Stop when acceptance criteria and required checks are satisfied; avoid speculative
polishing. Leave merge/deployment to the user's authorized workflow.

## Capability fallback

If custom roles or spawning are unavailable, state the limitation. Do not pretend
that an independent agent reviewed work. Offer a sequential role-based pass or
explain the client/version configuration needed. A prompt cannot enable missing
tools, network access, database access or browser capabilities.
