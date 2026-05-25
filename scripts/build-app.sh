#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
. "$REPO_DIR/scripts/lib/common.sh"

PKG="${PKG:-${1:-}}"
OUTPUT_ROOT="${OUTPUT_ROOT:-$REPO_DIR/output}"
APP_DIR="${APP_DIR:-$OUTPUT_ROOT/abp-studio-app}"
WORK_DIR="${WORK_DIR:-$OUTPUT_ROOT/work/build-app}"

[ -n "$PKG" ] || error "Set PKG=/path/abp-studio-3.0.2-stable-full.zip"

args=(build-app --pkg "$PKG" --output "$APP_DIR" --work-dir "$WORK_DIR")
if [ -n "${NATIVE_OVERRIDES:-}" ]; then
  args+=(--native-overrides "$NATIVE_OVERRIDES")
fi
if [ -n "${RUNTIME_ROOT:-}" ]; then
  args+=(--runtime-root "$RUNTIME_ROOT")
fi
if [ -n "${FIXTURE_PAYLOAD_DIR:-}" ]; then
  args+=(--fixture-payload-dir "$FIXTURE_PAYLOAD_DIR")
fi

run_tool "${args[@]}"
