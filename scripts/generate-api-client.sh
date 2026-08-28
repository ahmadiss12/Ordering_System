#!/usr/bin/env bash
#
# Regenerates the TypeScript API client from the API's own OpenAPI document.
#
# Run this after changing anything about the API surface — a route, a DTO, a status code. CI
# runs the same script and fails if the committed output differs, so a forgotten regeneration
# is caught in review rather than as a runtime "undefined" in an Angular app.
#
# Needs no running API and no database: the document is produced by building the project.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The generator boots the real host to read its routes, and this application refuses to boot
# without these. They exist for the length of this build and never reach a running server.
export Jwt__SigningKey="build-time-openapi-generation-placeholder-not-a-secret"
export ConnectionStrings__Default="Server=none;Database=none;Trusted_Connection=True"

echo "==> Building the API and emitting its OpenAPI document"
dotnet build "$root/api/src/OrderingSystem.Api" --nologo -p:GenerateOpenApiDocument=true

echo "==> Generating the TypeScript client"
if ! command -v nswag >/dev/null 2>&1; then
  echo "    installing NSwag..."
  dotnet tool install --global NSwag.ConsoleCore --version 14.7.0
fi
( cd "$root/scripts" && nswag run nswag.json )

echo "==> Done"
echo "    document: api/openapi/OrderingSystem.Api.json"
echo "    client:   web/projects/shared/api-client/src/lib/api-client.ts"
