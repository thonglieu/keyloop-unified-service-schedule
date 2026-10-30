#!/bin/sh
set -u

# Run both suites and propagate failure to Compose, retaining both TRX reports.
unit_status=0
integration_status=0
dotnet test tests/Scheduler.UnitTests --configuration Release --no-build --no-restore \
    --logger 'trx;LogFileName=unit.trx' --results-directory /test-results || unit_status=$?
dotnet test tests/Scheduler.IntegrationTests --configuration Release --no-build --no-restore \
    --logger 'trx;LogFileName=integration.trx' --results-directory /test-results || integration_status=$?

if [ "$unit_status" -ne 0 ]; then
    exit "$unit_status"
fi
exit "$integration_status"
