#!/usr/bin/env bash
# scripts/scan-dependencies-security.sh
# Fase 43 — Escaneo básico de dependencias con vulnerabilidades conocidas.
#
# IMPORTANTE: Solo reporta hallazgos. No actualiza paquetes.
# No ejecuta: npm audit fix · dotnet add package · dotnet restore (no requerido)
#
# Exit codes:
#   0 — sin vulnerabilidades Moderate/High/Critical
#   1 — vulnerabilidades encontradas (revisar antes de preproducción)
#   2 — no se pudo ejecutar (herramienta faltante o repo mal posicionado)
set -euo pipefail

# ── colores ─────────────────────────────────────────────────────────────────
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

header() { echo -e "\n${YELLOW}═══ $* ═══${NC}"; }
ok()     { echo -e "${GREEN}[OK]${NC}   $*"; }
warn()   { echo -e "${YELLOW}[WARN]${NC} $*"; }
fail()   { echo -e "${RED}[FAIL]${NC} $*"; }
info()   { echo -e "${CYAN}━━━ $* ━━━${NC}"; }

# ── verificar raíz del repo ──────────────────────────────────────────────────
if [[ ! -f "backend/Xpay.Api/Xpay.Api.csproj" ]]; then
  fail "Ejecutar desde la raíz del repositorio XPAY MVP (donde está backend/Xpay.Api/)"
  exit 2
fi

DOTNET_VULNS=0
NPM_VULNS=0
TOOL_ERROR=0

echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║  XPAY MVP — Escaneo básico de dependencias vulnerables       ║"
echo "║  Fase 43 | Solo reporta. No actualiza paquetes.              ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# ── Backend .NET ─────────────────────────────────────────────────────────────
header "Backend .NET — dotnet list package --vulnerable --include-transitive"

if ! command -v dotnet &>/dev/null; then
  fail "dotnet CLI no encontrado — instalar .NET SDK 8"
  TOOL_ERROR=1
else
  DOTNET_OUTPUT=$(dotnet list backend/Xpay.Api/Xpay.Api.csproj package \
    --vulnerable --include-transitive 2>&1 || true)
  echo "$DOTNET_OUTPUT"
  if echo "$DOTNET_OUTPUT" | grep -qiE "(Moderate|High|Critical)"; then
    DOTNET_VULNS=1
    warn "Vulnerabilidades Moderate/High/Critical encontradas en dependencias NuGet"
  else
    ok "Sin vulnerabilidades Moderate/High/Critical en dependencias .NET"
  fi
fi

echo ""

# ── Frontend npm ─────────────────────────────────────────────────────────────
header "Frontend npm — npm audit --audit-level=moderate"

FRONTEND_DIR="frontend/xpay-admin"

if ! command -v npm &>/dev/null; then
  fail "npm no encontrado — instalar Node.js"
  TOOL_ERROR=1
elif [[ ! -f "$FRONTEND_DIR/package-lock.json" ]]; then
  warn "package-lock.json no encontrado en $FRONTEND_DIR"
  warn "npm audit requiere lockfile — ejecutar 'npm install' en $FRONTEND_DIR primero"
  TOOL_ERROR=1
else
  ORIG_DIR=$(pwd)
  cd "$FRONTEND_DIR"
  NPM_EXIT=0
  npm audit --audit-level=moderate 2>&1 || NPM_EXIT=$?
  cd "$ORIG_DIR"
  if [[ $NPM_EXIT -ne 0 ]]; then
    NPM_VULNS=1
    warn "Vulnerabilidades Moderate+ encontradas en dependencias npm"
  else
    ok "Sin vulnerabilidades Moderate+ en dependencias npm"
  fi
fi

echo ""

# ── Resumen ──────────────────────────────────────────────────────────────────
header "Resumen"

if [[ $TOOL_ERROR -eq 1 ]]; then
  fail "Escaneo incompleto por falta de herramientas o configuración"
  echo ""
  fail "Resolver los errores anteriores y volver a ejecutar el script — exit 2"
  exit 2
fi

if [[ $DOTNET_VULNS -eq 1 || $NPM_VULNS -eq 1 ]]; then
  echo ""
  warn "HALLAZGOS ENCONTRADOS — acción requerida antes de preproducción"
  warn "• High/Critical: NO avanzar a dinero real sin aprobación explícita del Security Lead"
  warn "• Moderate:      registrar decisión de riesgo documentada en el acta de QA"
  warn "• NO ejecutar npm audit fix ni dotnet add package sin evaluación explícita"
  warn "• Adjuntar esta salida como evidencia en el package de preproducción"
  echo ""
  exit 1
else
  ok "Sin vulnerabilidades Moderate/High/Critical detectadas en este escaneo — exit 0"
  exit 0
fi
