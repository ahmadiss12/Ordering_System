<#
.SYNOPSIS
    Regenerates the TypeScript API client from the API's own OpenAPI document.

.DESCRIPTION
    Run after changing anything about the API surface. CI runs the same steps and fails if the
    committed output differs, so a forgotten regeneration is caught in review rather than as a
    runtime "undefined" in an Angular app. Needs no running API and no database.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# The generator boots the real host to read its routes, and this application refuses to boot
# without these. They exist for the length of this build and never reach a running server.
$env:Jwt__SigningKey = 'build-time-openapi-generation-placeholder-not-a-secret'
$env:ConnectionStrings__Default = 'Server=none;Database=none;Trusted_Connection=True'

Write-Host '==> Building the API and emitting its OpenAPI document' -ForegroundColor Cyan
dotnet build (Join-Path $root 'api/src/OrderingSystem.Api') --nologo -p:GenerateOpenApiDocument=true

Write-Host '==> Generating the TypeScript client' -ForegroundColor Cyan

# The global tool shim lands here, but putting it on PATH is left to the shell profile - so a
# fresh session installs the tool and then cannot find it. Do it ourselves rather than depending
# on how the machine happens to be set up.
$toolPath = if ($env:DOTNET_TOOLS_PATH) { $env:DOTNET_TOOLS_PATH } else { Join-Path $HOME '.dotnet/tools' }
$env:PATH = "$toolPath$([IO.Path]::PathSeparator)$env:PATH"

if (-not (Get-Command nswag -ErrorAction SilentlyContinue)) {
    Write-Host '    installing NSwag...' -ForegroundColor DarkGray
    dotnet tool install --global NSwag.ConsoleCore --version 14.7.0
}
Push-Location (Join-Path $root 'scripts')
try { nswag run nswag.json } finally { Pop-Location }

Write-Host '==> Done' -ForegroundColor Green
