#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPTS_DIR="$REPO_ROOT/scripts"

echo ""
echo "========================================"
echo "  XPAY MVP — QA Artifact Build (full)"
echo "========================================"
echo ""

echo "--- Step 1 / 2 : Backend ---"
echo ""
bash "$SCRIPTS_DIR/build-backend-qa.sh"

echo ""
echo "--- Step 2 / 2 : Frontend ---"
echo ""
bash "$SCRIPTS_DIR/build-frontend-qa.sh"

echo ""
echo "========================================"
echo "  XPAY QA artifacts ready:"
echo ""
echo "    artifacts/backend-qa   — .NET 8 publish output"
echo "    artifacts/frontend-qa  — Vite static build (dist/)"
echo "========================================"
echo ""
echo "  NOTE: These artifacts are NOT deployed by this script."
echo "  Follow docs/QA_DEPLOYMENT_RUNBOOK.md for deployment steps."
