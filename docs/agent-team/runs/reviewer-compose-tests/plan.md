# Plan

- reviewer_test_compose: new compose.tests.yaml, Dockerfile.tests, optional runner.
- Main: README, run records and final execution.
- Independent reviewer: read-only isolation, exit-status and artifact review.

Acceptance: one Compose command runs unit and real-provider integration tests
without host SDK/PostgreSQL; failures propagate; TRX survives cleanup; test storage
is isolated from demo data. Validate configuration and execute the Docker path.
