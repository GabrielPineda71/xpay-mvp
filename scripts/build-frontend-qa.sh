#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FRONTEND_DIR="frontend/xpay-admin"
OUTPUT="artifacts/frontend-qa"

cd "$REPO_ROOT"

echo "========================================"
echo "  XPAY QA — Frontend Build"
echo "========================================"
echo "  Source : $FRONTEND_DIR"
echo "  Output : $OUTPUT"
echo "========================================"
echo ""

if [ ! -f "$FRONTEND_DIR/.env" ]; then
  echo "ERROR: No .env file found at $FRONTEND_DIR/.env"
  echo ""
  echo "  No .env file found. Copy .env.qa.example to .env and set"
  echo "  VITE_API_BASE_URL before building QA."
  echo ""
  echo "  Example:"
  echo "    cp $FRONTEND_DIR/.env.qa.example $FRONTEND_DIR/.env"
  echo "    # Edit .env and confirm VITE_API_BASE_URL points to QA backend"
  echo ""
  exit 1
fi

echo "==> .env found — verifying VITE_API_BASE_URL..."
VITE_URL=$(grep -E '^VITE_API_BASE_URL=' "$FRONTEND_DIR/.env" | cut -d'=' -f2- || true)
if [ -z "$VITE_URL" ]; then
  echo "WARNING: VITE_API_BASE_URL is not set in $FRONTEND_DIR/.env"
  echo "         The frontend will fall back to http://localhost:5000"
fi
echo "  VITE_API_BASE_URL = ${VITE_URL:-'(not set — will use fallback localhost:5000)'}"
echo ""

echo "==> Installing npm dependencies..."
cd "$FRONTEND_DIR"
npm install

echo "==> Building frontend (Vite)..."
npm run build

cd "$REPO_ROOT"

echo "==> Copying dist/ to $OUTPUT..."
rm -rf "$OUTPUT"
cp -r "$FRONTEND_DIR/dist" "$OUTPUT"

echo ""
echo "========================================"
echo "  ✓ Frontend QA artifact ready at $OUTPUT"
echo "========================================"
echo ""
echo "  NOTE: This script does NOT deploy."
echo "  To deploy, follow docs/QA_DEPLOYMENT_RUNBOOK.md."
