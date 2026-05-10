#!/usr/bin/env bash
set -euo pipefail

RID="${1:-linux-arm64}"
OUT_DIR="../../artifacts/publish/EflayGameSaveManager.Avalonia/${RID}"

dotnet publish ./EflayGameSaveManager.Avalonia.csproj \
  -c Release \
  -r "${RID}" \
  -f net10.0 \
  --self-contained true \
  /p:PublishAot=true \
  /p:PublishTrimmed=true \
  /p:InvariantGlobalization=true \
  -o "${OUT_DIR}"

echo
echo "Published ${RID} build to ${OUT_DIR}"
