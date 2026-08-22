<#
.SYNOPSIS
    Checks that this machine can build and run the ordering system, end to end.

.DESCRIPTION
    Run this after cloning. Every step prints what it is doing and whether it passed,
    so a failure tells you which piece of the setup is missing rather than just
    "build failed".

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
#>

[CmdletBinding()]
param(
    [string]$SaPassword = $(if ($env:MSSQL_SA_PASSWORD) { $env:MSSQL_SA_PASSWORD } else { 'Your_Strong_Passw0rd' }),
    [int]$ApiPort = 5080,
    [switch]$SkipApi
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiRoot  = Join-Path $repoRoot 'api'
$solution = Join-Path $apiRoot 'OrderingSystem.slnx'
$results  = [ordered]@{}

function Write-Head($text) {
    Write-Host ''
    Write-Host ('-' * 68) -ForegroundColor DarkGray
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host ('-' * 68) -ForegroundColor DarkGray
}

function Write-Step($name, $ok, $detail) {
    $script:results[$name] = $ok
    if ($ok) {
        Write-Host '  PASS  ' -ForegroundColor Green -NoNewline
    } else {
        Write-Host '  FAIL  ' -ForegroundColor Red -NoNewline
    }
    Write-Host ("{0,-34}" -f $name) -NoNewline
    if ($detail) { Write-Host $detail -ForegroundColor DarkGray } else { Write-Host '' }
}

Write-Host ''
Write-Host '  Ordering System - environment check' -ForegroundColor White
Write-Host "  repository: $repoRoot" -ForegroundColor DarkGray
Write-Host "  started:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray

# ---------------------------------------------------------------- prerequisites
Write-Head '1. Prerequisites'

$dotnetOk = $false
$sdks = & dotnet --list-sdks 2>$null
if ($LASTEXITCODE -eq 0) {
    $net10 = $sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1
    $dotnetOk = [bool]$net10
    Write-Step '.NET 10 SDK' $dotnetOk $(if ($net10) { ($net10 -split ' ')[0] } else { 'install from https://dot.net - the project targets net10.0' })
} else {
    Write-Step '.NET 10 SDK' $false 'dotnet not found on PATH'
}

$dockerOk = $false
$dockerVersion = & docker version --format '{{.Server.Version}}' 2>$null
if ($LASTEXITCODE -eq 0 -and $dockerVersion) {
    $dockerOk = $true
    Write-Step 'Docker daemon' $true "server $dockerVersion"
} else {
    Write-Step 'Docker daemon' $false 'start Docker Desktop, then run this again'
}

$gitVersion = & git --version 2>$null
Write-Step 'git' ($LASTEXITCODE -eq 0) $gitVersion

if (-not $dotnetOk -or -not $dockerOk) {
    Write-Host ''
    Write-Host '  Stopping: the prerequisites above must be installed first.' -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- containers
Write-Head '2. Database and mail catcher'

$env:MSSQL_SA_PASSWORD = $SaPassword
$composeFile = Join-Path (Join-Path $repoRoot 'docker') 'docker-compose.yml'
& docker compose -f $composeFile up -d 2>&1 | Out-String | Write-Verbose

# Check each container rather than compose's aggregate exit code: if one image
# fails to pull, we want to know which, not just that "something" went wrong.
function Test-Container($name) {
    $running = & docker ps --filter "name=$name" --format '{{.Names}}' 2>$null
    return ($running -match [regex]::Escape($name))
}

Write-Step 'SQL Server container' (Test-Container 'ordering-sqlserver') 'localhost,1433'
Write-Step 'Mailpit container'   (Test-Container 'ordering-mailpit')   'http://localhost:8025 - catches password-reset email'

Write-Host '        waiting for SQL Server to accept connections...' -ForegroundColor DarkGray
$sqlReady = $false
foreach ($attempt in 1..40) {
    & docker exec ordering-sqlserver /opt/mssql-tools18/bin/sqlcmd `
        -S localhost -U sa -P $SaPassword -N -C -Q 'SELECT 1' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $sqlReady = $true; break }
    Start-Sleep -Seconds 3
}
Write-Step 'SQL Server reachable' $sqlReady $(if ($sqlReady) { 'localhost,1433' } else { 'never became ready - check: docker logs ordering-sqlserver' })

# ---------------------------------------------------------------- build
Write-Head '3. Build'

$buildOutput = & dotnet build $solution --nologo 2>&1 | Out-String
$buildOk = $LASTEXITCODE -eq 0
$warnings = ([regex]::Match($buildOutput, '(\d+) Warning\(s\)')).Groups[1].Value
Write-Step 'solution builds' $buildOk "$warnings warnings, 7 projects"
if (-not $buildOk) { $buildOutput -split "`n" | Select-String 'error' | Select-Object -First 8 }

# ---------------------------------------------------------------- migrations
Write-Head '4. Database schema'

& dotnet tool list --global 2>$null | Select-String 'dotnet-ef' | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host '        installing dotnet-ef...' -ForegroundColor DarkGray
    & dotnet tool install --global dotnet-ef --version 10.* 2>&1 | Out-String | Write-Verbose
}

$infrastructureProject = Join-Path (Join-Path $apiRoot 'src') 'OrderingSystem.Infrastructure'
$env:ORDERING_CONNECTION = "Server=localhost,1433;Database=OrderingSystem;User Id=sa;Password=$SaPassword;TrustServerCertificate=True"
& dotnet ef database update `
    --project $infrastructureProject `
    --startup-project $infrastructureProject `
    --no-color 2>&1 | Out-String | Write-Verbose
Write-Step 'migrations applied' ($LASTEXITCODE -eq 0) 'InitialCreate'

function Invoke-Sql($query) {
    (& docker exec ordering-sqlserver /opt/mssql-tools18/bin/sqlcmd `
        -S localhost -U sa -P $SaPassword -N -C -d OrderingSystem -h -1 -W `
        -Q "SET NOCOUNT ON; $query" 2>$null | Out-String).Trim()
}

$tables = Invoke-Sql "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'"
Write-Step 'tables created' ($tables -eq '26') "$tables of 26"

$checks = Invoke-Sql 'SELECT COUNT(*) FROM sys.check_constraints'
Write-Step 'check constraints' ($checks -eq '12') "$checks of 12"

$maxCols = Invoke-Sql "SELECT COUNT(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id JOIN sys.tables t ON t.object_id = c.object_id WHERE ty.name = 'nvarchar' AND c.max_length = -1 AND t.is_ms_shipped = 0"
Write-Step 'no unbounded text columns' ($maxCols -eq '0') "$maxCols nvarchar(max) found"

Write-Host '        seeding demo data...' -ForegroundColor DarkGray
$env:ConnectionStrings__Default = "Server=localhost,1433;Database=OrderingSystem;User Id=sa;Password=$SaPassword;TrustServerCertificate=True"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
& dotnet run --project (Join-Path (Join-Path $apiRoot 'src') 'OrderingSystem.Api') --no-launch-profile -- --seed 2>&1 |
    Out-String | Write-Verbose
$restaurantCount = Invoke-Sql 'SELECT COUNT(*) FROM Restaurants'
$itemCount = Invoke-Sql 'SELECT COUNT(*) FROM MenuItems'
Write-Step 'demo data seeded' ($restaurantCount -eq '3') "$restaurantCount restaurants, $itemCount menu items"

# ---------------------------------------------------------------- tests
Write-Head '5. Tests'

$testOutput = & dotnet test $solution --no-build --nologo 2>&1 | Out-String
$testOk = $LASTEXITCODE -eq 0
# Every test assembly reports its own line, so sum them rather than showing the first -
# reporting one assembly's result as though it were the whole suite is how a red test hides.
$assemblyLines = @($testOutput -split "`n" | Select-String 'Passed!|Failed!')
$totalPassed = 0; $totalFailed = 0
foreach ($line in $assemblyLines) {
    if ($line -match 'Failed:\s+(\d+)') { $totalFailed += [int]$Matches[1] }
    if ($line -match 'Passed:\s+(\d+)') { $totalPassed += [int]$Matches[1] }
}
Write-Step 'test suite' $testOk "$totalPassed passed, $totalFailed failed, across $($assemblyLines.Count) assemblies"

# ---------------------------------------------------------------- api
if (-not $SkipApi) {
    Write-Head '6. API'

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
    # -WindowStyle exists only on Windows PowerShell, so it is added conditionally
    # rather than unconditionally - otherwise this line throws on PowerShell 7 elsewhere.
    $startArgs = @{
        FilePath     = 'dotnet'
        ArgumentList = @('run', '--project',
                         (Join-Path (Join-Path $apiRoot 'src') 'OrderingSystem.Api'),
                         '--no-launch-profile')
        PassThru     = $true
    }
    if ($PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows) { $startArgs.WindowStyle = 'Hidden' }
    $api = Start-Process @startArgs

    $healthy = $false
    foreach ($attempt in 1..30) {
        try {
            $response = Invoke-RestMethod "http://localhost:$ApiPort/health" -TimeoutSec 3
            if ($response.status -eq 'ok') { $healthy = $true; break }
        } catch { Start-Sleep -Seconds 2 }
    }
    Write-Step 'API responds' $healthy "GET http://localhost:$ApiPort/health"

    $openApiOk = $false
    if ($healthy) {
        try {
            $doc = Invoke-RestMethod "http://localhost:$ApiPort/openapi/v1.json" -TimeoutSec 5
            $openApiOk = [bool]$doc.openapi
        } catch { }
    }
    Write-Step 'OpenAPI document' $openApiOk $(if ($openApiOk) { "openapi $($doc.openapi)" } else { 'not served' })

    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------- summary
Write-Head 'Summary'

$failed = @($results.GetEnumerator() | Where-Object { -not $_.Value })
foreach ($entry in $results.GetEnumerator()) {
    $mark  = if ($entry.Value) { 'ok  ' } else { 'FAIL' }
    $color = if ($entry.Value) { 'Green' } else { 'Red' }
    Write-Host "    $mark  $($entry.Key)" -ForegroundColor $color
}

Write-Host ''
if ($failed.Count -eq 0) {
    Write-Host '  Everything works on this machine.' -ForegroundColor Green
    Write-Host '  Mailpit UI: http://localhost:8025   Database: localhost,1433 (sa)' -ForegroundColor DarkGray
    Write-Host ''
    exit 0
}

Write-Host "  $($failed.Count) check(s) failed: $($failed.Name -join ', ')" -ForegroundColor Red
Write-Host '  Re-run with -Verbose to see the underlying command output.' -ForegroundColor DarkGray
Write-Host ''
exit 1
