#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-x64}"
CONFIGURATION="${2:-Release}"
OUTPUT_DIR="${3:-./publish/${RID}}"

dotnet publish "azure-web-log-downloader.app/azure-web-log-downloader.app.csproj" \
  -c "${CONFIGURATION}" \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:InvariantGlobalization=true \
  -p:EnableCompressionInSingleFile=true \
  -o "${OUTPUT_DIR}"

echo "Published single-file executable to ${OUTPUT_DIR}"
