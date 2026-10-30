# Verification

## Resumed verification (2026-10-29)

- `dotnet build Keyloop.ServiceScheduler.sln --no-restore`: passed, zero warnings/errors.
- Unit suite: 28 passed (`artifacts/reviewer-simple-setup/unit-resumed.trx`).
- ReviewerAuthenticationTests: 7 passed (`auth-resumed.trx` in the same directory).
- PostgreSQL 17.2 suite: initial sandbox run 157 passed / 2 subprocess timeouts;
  unrestricted rerun 158 passed / 1 telemetry assertion failure
  (`integration-resumed-unrestricted.trx`). After fault-injection callback cleanup,
  final unrestricted suite passed **159/159**, zero skipped (`integration-final.trx`).
  Command: `dotnet test tests/Scheduler.IntegrationTests --no-build --no-restore
  --logger 'trx;LogFileName=integration-final.trx' --results-directory
  artifacts/reviewer-simple-setup`, with the dedicated test database on port 55432.
  Reduced reproduction and 29/29 focused results are recorded in
  `telemetry-reproduction.txt` and `telemetry-cleanup.trx` alongside the final TRX.
- `scripts/start-reviewer.ps1`: successful local Docker startup at port 5080.
  Live HTTP smoke: three JWT accounts issued, Admin fixture retrieval and Customer
  owned-vehicle retrieval passed. Tokens were not recorded.
- Repeated startup: passed; `.env` hash and fixture namespace unchanged, fresh
  tokens issued. Log: `artifacts/reviewer-simple-setup/start-repeat.log`.
- Lifecycle first run: clean setup and restart persistence passed; failure-guard
  diagnostic assertion failed because reviewer authentication intercepted the
  deliberately disabled demo mode. Test override corrected; final rerun passed
  (project `keyloop-reviewer-test-20261029130307`, exit 0): invalid credentials
  rejected, fresh migration/seed/readiness passed, all three appointments preserved
  across restart, migration guard failure prevented API startup. Test containers
  stopped and their volume retained.
- Independent read-only review found no blocking setup defects. PowerShell parsing
  and shell line endings passed; Bash runtime execution remains unverified.
  Follow-up review approved fault-injection cleanup and the migration override;
  original telemetry and guard assertions remain intact. Fresh .env generation
  was inspected; executed startup reused existing configuration.

Existing uncommitted backend/client-removal changes predate this resumed work.
No deployment, commit, reset or production migration performed.
