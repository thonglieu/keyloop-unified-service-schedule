$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $repositoryRoot '.env'

function New-DatabasePassword {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}

function Get-FileSetting([string]$Name) {
    if (!(Test-Path -LiteralPath $envPath)) { return $null }
    $value = $null
    foreach ($line in [IO.File]::ReadAllLines($envPath)) {
        if ($line -match ('^\s*(?:export\s+)?' + [regex]::Escape($Name) + '\s*=(.*)$')) {
            $value = $Matches[1].Trim()
            # Values here are simple .env settings, never evaluated as PowerShell.
            $value = ($value -replace '\s+#.*$', '').Trim().Trim('"').Trim("'")
        }
    }
    return $value
}

function Get-EffectiveSetting([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if ($null -ne $value) { return $value }
    return Get-FileSetting $Name
}

Push-Location $repositoryRoot
try {
    & docker compose version
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 is required.' }
    $containerOs = & docker info --format '{{.OSType}}'
    if ($LASTEXITCODE -ne 0 -or $containerOs -ne 'linux') { throw 'Start Docker with Linux containers, then rerun this command.' }

    $externalIdentity = $false
    foreach ($name in @('REVIEWER_JWT_AUTHORITY', 'REVIEWER_JWT_ISSUER', 'REVIEWER_JWT_AUDIENCE')) {
        if (![string]::IsNullOrWhiteSpace((Get-EffectiveSetting $name))) { $externalIdentity = $true }
    }
    $defaultAuth = if ($externalIdentity) { 'false' } else { 'true' }
    if (!(Test-Path -LiteralPath $envPath)) {
        $content = @(
            '# Generated local reviewer configuration. Keep this file for later runs.'
            ('REVIEWER_DB_ADMIN_PASSWORD=' + (New-DatabasePassword))
            ('REVIEWER_DB_PASSWORD=' + (New-DatabasePassword))
            ('REVIEWER_AUTH_ENABLED=' + $defaultAuth)
            'REVIEWER_JWT_AUTHORITY='
            'REVIEWER_JWT_ISSUER='
            'REVIEWER_JWT_AUDIENCE='
            'REVIEWER_API_PORT=5080'
        ) -join "`n"
        # CreateNew prevents overwriting a configuration created by another process.
        $stream = [IO.File]::Open($envPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes($content + "`n")
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
        Write-Host 'Created .env with generated database credentials.'
    } elseif ($null -eq (Get-FileSetting 'REVIEWER_AUTH_ENABLED') -and
              $null -eq [Environment]::GetEnvironmentVariable('REVIEWER_AUTH_ENABLED', 'Process') -and
              !$externalIdentity) {
        [IO.File]::AppendAllText($envPath, "`nREVIEWER_AUTH_ENABLED=true`n")
        Write-Host 'Enabled local reviewer authentication; existing credentials were preserved.'
    }

    foreach ($name in @('REVIEWER_DB_ADMIN_PASSWORD', 'REVIEWER_DB_PASSWORD')) {
        $value = Get-EffectiveSetting $name
        if ($value -notmatch '^[A-Za-z0-9]+$') {
            throw "$name must contain ASCII letters and digits. Existing .env was preserved; replace its placeholder manually."
        }
    }
    & docker compose config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose configuration is invalid.' }
    & docker compose up --build --wait --wait-timeout 180
    if ($LASTEXITCODE -ne 0) { throw 'Startup failed. Inspect docker compose ps -a and docker compose logs db migrate seed api. Data was preserved.' }

    $port = Get-EffectiveSetting 'REVIEWER_API_PORT'
    if ([string]::IsNullOrWhiteSpace($port)) { $port = '5080' }
    Write-Host "Swagger: http://localhost:$port/swagger"
    if ((Get-EffectiveSetting 'REVIEWER_AUTH_ENABLED') -eq 'true') {
        Write-Host 'In Swagger, execute POST /api/demo/auth to obtain local Admin and Customer tokens.'
        Write-Host 'Paste an access token into Authorize, then test the business endpoints.'
    } else {
        Write-Host 'External authentication mode: use an access token from your configured identity provider.'
    }
    Write-Host 'Stop with docker compose down. Keep .env and the database volume for later runs.'
} finally {
    Pop-Location
}
