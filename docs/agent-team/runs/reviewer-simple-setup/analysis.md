# Analysis

User requests simpler PostgreSQL and authentication setup for reviewers. Existing
Compose handles database creation/migrations/seeding but requires manual passwords
and an external identity provider. Implement an explicit local Development-only
signed JWT issuer and one-command scripts, preserving real bearer/RBAC/ownership.
Scope: D-02/D-03/D-04 usability plus SEC-01/SEC-02 regression checks.
Baseline commit: 7def013b6c1fb51a1903a1c96c5a4378208cf93d. Working tree already has
extensive backend/documentation changes and incomplete idempotency work. Baseline
build previously failed on two missing transaction methods; do not erase that work.
