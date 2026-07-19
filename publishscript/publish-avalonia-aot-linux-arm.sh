#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RID="${1:-linux-arm64}"
PROJECT="${REPO_ROOT}/src/EflayGameSaveManager.Avalonia/EflayGameSaveManager.Avalonia.csproj"
OUT_DIR="${REPO_ROOT}/artifacts/publish/EflayGameSaveManager.Avalonia/${RID}"

dotnet publish "${PROJECT}" \
  -c Release \
  -r "${RID}" \
  -f net10.0 \
  --self-contained true \
  /p:PublishAot=true \
  /p:PublishTrimmed=true \
  /p:DebugType=None \
  /p:DebugSymbols=false \
  /p:InvariantGlobalization=true \
  -o "${OUT_DIR}"

find "${OUT_DIR}" -type f \
  \( -name '*.pdb' -o -name '*.dbg' -o -name '*.ipdb' -o -name '*.iobj' -o -name '*.ilk' -o -name '*.map' \) \
  -delete
find "${OUT_DIR}" -type d -name '*.dSYM' -prune -exec rm -rf {} +

echo
echo "Published ${RID} build to ${OUT_DIR} (debug symbols removed)"
