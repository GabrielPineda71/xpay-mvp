#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="backend/Xpay.Api/Xpay.Api.csproj"
OUTPUT="artifacts/backend-qa"

cd "$REPO_ROOT"

echo "========================================"
echo "  XPAY QA — Backend Build"
echo "========================================"
echo "  Project : $PROJECT"
echo "  Output  : $OUTPUT"
echo "========================================"
echo ""

echo "==> Cleaning previous artifact..."
rm -rf "$OUTPUT"

echo "==> Restoring dependencies..."
dotnet restore "$PROJECT"

echo "==> Building (Release)..."
dotnet build "$PROJECT" -c Release --no-restore

echo "==> Publishing to $OUTPUT..."
dotnet publish "$PROJECT" -c Release -o "$OUTPUT" --no-build

echo ""
echo "========================================"
echo "  ✓ Backend QA artifact ready at $OUTPUT"
echo "========================================"
echo ""
echo "  NOTE: This script does NOT deploy."
echo "  To deploy, follow docs/QA_DEPLOYMENT_RUNBOOK.md."
