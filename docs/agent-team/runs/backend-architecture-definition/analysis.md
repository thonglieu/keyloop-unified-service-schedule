# Architecture analysis — 2026-10-27

User requests architecture definition before modifying service code. Target adds
DDD/Clean/CQRS/in-memory events/JWT/RBAC/validation/middleware/errors/logging to
.NET 8/EF Core/PostgreSQL ACID. External token issuer confirmed by user. Customer
and Admin are documented initial role assumptions. Backend remains sole full layer.

Baseline commit 7def013 already had previous scope-correction docs modified and PDF/
run records untracked; preserve all. Analyst inspected existing source read-only:
controller reads DbContext, Infrastructure owns use cases, Domain has mutable entities
and an EF Design dependency, no CQRS/event/JWT layers. Existing database constraints,
transaction retry and interval/provider tests should survive incremental refactoring.
No source edits or runtime verification authorized in this design-only task.

> Documentation note (2026-10-30): the presentation script mentioned in the
> baseline below has since moved outside the repository.

## Current pass

Baseline reconfirmed: 7def013b6c1fb51a1903a1c96c5a4378208cf93d. Pre-existing
modified files: README.md, START_HERE.txt, PROJECT.md, ORCHESTRATOR.md,
demo-script.md, system-design.md and specs/feature.md. PDF, architecture document
and architecture/scope-correction runs were already untracked. Preserve these;
the full HEAD diff is not attributable to this pass.

Actual read-only subagents: DESIGN-SPEC (spec_analyst, Kierkegaard) mapped all 23
acceptance IDs; DESIGN-REVIEW (constraint_reviewer, Confucius) reviewed security,
transaction/event semantics and ownership. Main session alone edits documentation.

Reuse existing interval rules, provider exclusions, persistence and concurrency
tests. Missing architecture/security/event tests are new coverage, not reasons to
rebuild functioning booking behavior. Source inspection is not runtime evidence.
The existing retry loop reuses a DbContext; target attempts use fresh contexts.
The existing connection-failure test does not prove transactional rollback.

Reviewer found Customer POST could distinguish foreign from missing vehicles.
Design now requires equivalent 404 responses; Admin relationship mismatch remains
400. Clarified direct-handler roles, subject mapping, dual-role Admin precedence,
query mapping requirements, stable error codes, unknown commit outcomes and a
cooperative event budget. These are documented design choices, not implemented fixes.
Preserve current availability semantics: one window per resource must cover the
whole appointment; adjacent windows are not merged. No new material user choice
blocks design. Database patch compatibility must be resolved if a future probe
confirms the historical mismatch; issuer values are future configuration inputs.

Fresh PDF extraction was unavailable: python.exe could not execute, py reported
no installed Python, and pdftotext/mutool were absent. No packages installed.
This pass uses the current spec and historical PDF analysis; direct PDF revalidation
remains a recorded limitation rather than a newly verified source claim.
