# Plan

- reviewer_auth owns API auth/composition, new reviewer identity helper and focused
  auth tests. Explicit Development/demo/database guards; no production issuer.
- setup_scripts owns new PowerShell/Bash startup scripts, Compose and .env.example.
- build_repair owns existing incomplete idempotency infrastructure/migration and
  transaction test doubles; bounded prerequisite to building the requested setup.
- Main owns documentation/run records and integrated checks.
- Independent reviewer checks guards, persistence, secrets and startup usability
  after edits. Serialize builds/provider/lifecycle checks.

Acceptance: fresh local start generates credentials without overwriting existing
configuration; real PostgreSQL migration/seed gates remain; demo Admin/Customer JWTs
exercise protected routes; invalid environments cannot enable demo issuance;
external authentication stays available; repeat start preserves stored records.

## Resume ownership (2026-10-29)

- Main: integrated checks, START_HERE and shared run records.
- resume_review: read-only setup and final repair review.
- telemetry_fix: ApplicationBehaviorTests.cs fault-injection cleanup and
  verify-reviewer.ps1 migration-failure override only. EnvironmentTests.cs was
  inspected during diagnosis and restored without functional changes.
