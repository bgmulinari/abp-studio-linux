#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
. "$REPO_DIR/scripts/lib/common.sh"

require_command dpkg
require_command dpkg-deb

OUTPUT_ROOT="${OUTPUT_ROOT:-$REPO_DIR/output}"
APP_DIR="${APP_DIR:-$OUTPUT_ROOT/abp-studio-app}"
DIST_DIR="${DIST_DIR:-$OUTPUT_ROOT/dist}"
STAGING_ROOT="${STAGING_ROOT:-$DIST_DIR/deb-root}"
PACKAGE_VERSION="${PACKAGE_VERSION:-$(detect_app_version "$APP_DIR")}"
ARCH="$(map_deb_arch)"
OUTPUT="$DIST_DIR/abp-studio_${PACKAGE_VERSION}_${ARCH}.deb"
DPKG_DEB_LOG="$DIST_DIR/dpkg-deb.log"

run_tool stage-package \
  --format deb \
  --repo-root "$REPO_DIR" \
  --app-dir "$APP_DIR" \
  --staging-root "$STAGING_ROOT"

mkdir -p "$STAGING_ROOT/DEBIAN" "$DIST_DIR"
sed \
  -e "s/__VERSION__/$(sed_escape "$PACKAGE_VERSION")/g" \
  -e "s/__ARCH__/$(sed_escape "$ARCH")/g" \
  "$REPO_DIR/packaging/linux/control.template" > "$STAGING_ROOT/DEBIAN/control"
chmod 0644 "$STAGING_ROOT/DEBIAN/control"
cp "$REPO_DIR/packaging/linux/deb-postinst" "$STAGING_ROOT/DEBIAN/postinst"
cp "$REPO_DIR/packaging/linux/deb-prerm" "$STAGING_ROOT/DEBIAN/prerm"
cp "$REPO_DIR/packaging/linux/deb-postrm" "$STAGING_ROOT/DEBIAN/postrm"
chmod 0755 "$STAGING_ROOT/DEBIAN/postinst" "$STAGING_ROOT/DEBIAN/prerm" "$STAGING_ROOT/DEBIAN/postrm"

info "Building $OUTPUT"
info "Running dpkg-deb; this can take a while. Detailed output will be shown only if it fails"
if ! dpkg-deb --root-owner-group --build "$STAGING_ROOT" "$OUTPUT" >"$DPKG_DEB_LOG" 2>&1; then
  echo "[ERROR] dpkg-deb failed. Last 80 log lines:" >&2
  tail -n 80 "$DPKG_DEB_LOG" >&2 || true
  exit 1
fi
info "Built package: $OUTPUT"
