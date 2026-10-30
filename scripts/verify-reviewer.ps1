param(
    [int]$ApiPort = 5098
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = 'keyloop-reviewer-test-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$composeFile = Join-Path $repositoryRoot 'compose.yaml'
$failureOverride = Join-Path ([IO.Path]::GetTempPath()) ($project + '-failure.yaml')
$previousEnvironment = @{}
foreach ($name in @('REVIEWER_DB_ADMIN_PASSWORD', 'REVIEWER_DB_PASSWORD', 'REVIEWER_API_PORT')) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-Compose {
    param([string[]]$Command, [switch]$ExpectFailure, [string]$Override)
    $composeArguments = @('compose', '-p', $project, '-f', $composeFile)
    if ($Override) { $composeArguments += @('-f', $Override) }
    & docker @composeArguments @Command
    $result = $LASTEXITCODE
    if ($ExpectFailure) {
        if ($result -eq 0) { throw 'Expected Docker Compose to reject the invalid setup.' }
    } elseif ($result -ne 0) {
        throw "Docker Compose failed with exit code $result."
    }
}

function Invoke-DatabaseSql {
    param([string]$Sql)
    $rows = $Sql | & docker compose -p $project -f $composeFile exec -T db psql `
        -U scheduler_bootstrap -d keyloop_scheduler_demo -At -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'Database verification query failed.' }
    return ($rows -join "`n").Trim()
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw $Message }
}

function Get-PersistedState {
    $sql = @'
SELECT json_build_object(
    'migrations', (SELECT string_agg("MigrationId", ',' ORDER BY "MigrationId") FROM "__EFMigrationsHistory"),
    'fixtures', (SELECT string_agg("Id"::text, ',' ORDER BY "Id") FROM "DemoFixtures"),
    'appointments', (SELECT string_agg("Id"::text, ',' ORDER BY "Id") FROM "Appointments")
)::text;
'@
    return (Invoke-DatabaseSql $sql | ConvertFrom-Json)
}

try {
    & docker version --format '{{.Server.Version}}'
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable; no bootstrap checks were executed.' }
    # Ephemeral credentials stay in this process and are never printed or saved in the repository.
    $env:REVIEWER_DB_ADMIN_PASSWORD = [Guid]::NewGuid().ToString('N')
    $validAppPassword = [Guid]::NewGuid().ToString('N')
    $env:REVIEWER_DB_PASSWORD = 'invalid_password'
    $env:REVIEWER_API_PORT = [string]$ApiPort

    Invoke-Compose -Command @('config', '--quiet')
    Invoke-Compose -Command @('up', '--wait', '--wait-timeout', '60', 'db') -ExpectFailure
    $rejectedSetup = & docker compose -p $project -f $composeFile logs --no-color db
    if ($LASTEXITCODE -ne 0 -or ($rejectedSetup -join "`n") -notmatch 'ASCII letters and digits') {
        throw 'Database startup failed without the expected password-validation diagnostic.'
    }
    Write-Output 'PASS: invalid password fails before database initialization.'

    $env:REVIEWER_DB_PASSWORD = $validAppPassword
    Invoke-Compose -Command @('up', '--build', '--wait', '--wait-timeout', '180')
    $baseUrl = "http://127.0.0.1:$ApiPort"
    Assert-Equal 200 (Invoke-WebRequest "$baseUrl/health/ready" -UseBasicParsing).StatusCode 'API is not ready.'
    Assert-Equal 200 (Invoke-WebRequest "$baseUrl/swagger/v1/swagger.json" -UseBasicParsing).StatusCode 'Swagger is unavailable.'
    try {
        Invoke-WebRequest "$baseUrl/api/dealerships" -UseBasicParsing | Out-Null
        throw 'Protected business route unexpectedly allowed anonymous access.'
    } catch {
        if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
    Assert-Equal '17.2' (Invoke-DatabaseSql "SELECT split_part(current_setting('server_version'), ' ', 1);") 'PostgreSQL version differs from the pinned target.'
    Assert-Equal 'f|f|f|t' (Invoke-DatabaseSql "SELECT r.rolsuper, r.rolcreatedb, r.rolcreaterole, d.datdba = r.oid FROM pg_roles r CROSS JOIN pg_database d WHERE r.rolname = 'scheduler_app' AND d.datname = 'keyloop_scheduler_demo';") 'Application role must be a non-superuser database owner.'
    Assert-Equal '1' (Invoke-DatabaseSql 'SELECT count(*) FROM "DemoFixtures";') 'Clean startup should create one fixture namespace.'
    Assert-Equal '2' (Invoke-DatabaseSql 'SELECT count(*) FROM "Appointments";') 'Expected the two synthetic occupied-resource appointments.'
    $dbContainer = (& docker compose -p $project -f $composeFile ps -q db).Trim()
    $bindings = (& docker inspect $dbContainer --format '{{json .HostConfig.PortBindings}}').Trim()
    if ($LASTEXITCODE -ne 0 -or $bindings -notin @('{}', 'null')) { throw 'Reviewer database unexpectedly publishes a host port.' }
    Write-Output 'PASS: clean volume migrates/seeds, role is restricted, API is ready, Swagger works, anonymous routes stay protected.'

    # Insert a synthetic booking in the valid scenario to distinguish persisted reviewer data from seed rows.
    $bookingSql = @'
WITH fixture AS (SELECT "Json"::jsonb AS j FROM "DemoFixtures" ORDER BY "SeededAtUtc" DESC LIMIT 1)
INSERT INTO "Appointments" ("Id", "CustomerId", "VehicleId", "DealershipId", "ServiceTypeId", "TechnicianId", "ServiceBayId", "StartsAtUtc", "EndsAtUtc", "CreatedAtUtc")
SELECT gen_random_uuid(), (j->'customers'->0->>'customerId')::uuid, (j->'customers'->0->>'vehicleId')::uuid,
       (j->'scenarios'->'valid'->>'dealershipId')::uuid, (j->'scenarios'->'valid'->>'serviceTypeId')::uuid,
       (j->'scenarios'->'valid'->'technicianIds'->>0)::uuid, (j->'scenarios'->'valid'->'serviceBayIds'->>0)::uuid,
       (j->'scenarios'->'valid'->>'startsAt')::timestamptz,
       (j->'scenarios'->'valid'->>'startsAt')::timestamptz + make_interval(mins => (j->'scenarios'->'valid'->>'durationMinutes')::integer), now()
FROM fixture;
'@
    Invoke-DatabaseSql $bookingSql | Out-Null
    $before = Get-PersistedState
    Invoke-Compose -Command @('run', '--rm', 'migrate')
    $afterMigration = Get-PersistedState
    Assert-Equal $before.migrations $afterMigration.migrations 'Second migration changed applied migration history.'
    Assert-Equal $before.appointments $afterMigration.appointments 'Second migration changed stored appointments.'
    Invoke-Compose -Command @('down')
    Invoke-Compose -Command @('up', '--build', '--wait', '--wait-timeout', '180')
    $afterRestart = Get-PersistedState
    Assert-Equal $before.migrations $afterRestart.migrations 'Restart changed applied migration history.'
    Assert-Equal $before.fixtures $afterRestart.fixtures 'Restart replaced or duplicated valid fixture data.'
    Assert-Equal $before.appointments $afterRestart.appointments 'Restart lost stored appointments.'
    Assert-Equal '3' (Invoke-DatabaseSql 'SELECT count(*) FROM "Appointments";') 'Synthetic booking was not preserved.'
    Write-Output 'PASS: repeated migration and full down/up preserve migrations, fixture IDs and all three stored appointments.'

    Invoke-Compose -Command @('down')
    [IO.File]::WriteAllText($failureOverride, "services:`n  migrate:`n    environment:`n      Demo__Enabled: 'false'`n      ReviewerAuth__Enabled: 'false'`n")
    Invoke-Compose -Command @('up', '--wait', '--wait-timeout', '60') -Override $failureOverride -ExpectFailure
    $failedMigration = & docker compose -p $project -f $composeFile logs --no-color migrate
    if ($LASTEXITCODE -ne 0 -or ($failedMigration -join "`n") -notmatch 'Local setup requires Development, Demo.Enabled and the allowlisted demo database\.') {
        throw 'Migration failed without the expected setup-guard diagnostic.'
    }
    $migrationContainer = (& docker compose -p $project -f $composeFile ps -aq migrate).Trim()
    $migrationExit = & docker inspect $migrationContainer --format '{{.State.ExitCode}}'
    if ($LASTEXITCODE -ne 0 -or [int]$migrationExit -eq 0) { throw 'Migration container did not exit with failure.' }
    $runningApi = & docker compose -p $project -f $composeFile ps --status running -q api
    if ($LASTEXITCODE -ne 0 -or $runningApi) { throw 'API started despite the failed migration step.' }
    Write-Output 'PASS: failed migration step prevents API startup.'
    Invoke-Compose -Command @('down')
    Write-Output "PASS: reviewer bootstrap verification complete; preserved test volume for project $project."
} finally {
    if (Test-Path -LiteralPath $failureOverride) { Remove-Item -LiteralPath $failureOverride }
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
