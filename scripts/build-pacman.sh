#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
. "$REPO_DIR/scripts/lib/common.sh"

require_command makepkg

if [ "$(id -u)" -eq 0 ]; then
  error "makepkg cannot run as root"
fi

OUTPUT_ROOT="${OUTPUT_ROOT:-$REPO_DIR/output}"
APP_DIR="${APP_DIR:-$OUTPUT_ROOT/abp-studio-app}"
DIST_DIR="${DIST_DIR:-$OUTPUT_ROOT/dist}"
WORK_DIR="${WORK_DIR:-$OUTPUT_ROOT/work}"
PACKAGE_VERSION="${PACKAGE_VERSION:-$(detect_app_version "$APP_DIR")}"
ARCH="$(map_pacman_arch)"
PKGVER="${PACKAGE_VERSION%%+*}"
PKGREL="1"

mkdir -p "$WORK_DIR"
BUILD_ROOT="$(mktemp -d "$WORK_DIR/pacman-build.XXXXXXXXXX")"
trap 'rm -rf "$BUILD_ROOT"' EXIT
STAGING_ROOT="$BUILD_ROOT/staging"
MAKEPKG_LOG="$BUILD_ROOT/makepkg.log"

run_tool stage-package \
  --format pacman \
  --repo-root "$REPO_DIR" \
  --app-dir "$APP_DIR" \
  --staging-root "$STAGING_ROOT"

sed \
  -e "s/__PKGVER__/$(sed_escape "$PKGVER")/g" \
  -e "s/__PKGREL__/$(sed_escape "$PKGREL")/g" \
  -e "s/__ARCH__/$(sed_escape "$ARCH")/g" \
  -e "s/__STAGING_ROOT__/$(sed_escape "$STAGING_ROOT")/g" \
  "$REPO_DIR/packaging/linux/PKGBUILD.template" > "$BUILD_ROOT/PKGBUILD"
cp "$REPO_DIR/packaging/linux/abp-studio.install" "$BUILD_ROOT/abp-studio.install"

mkdir -p "$DIST_DIR"
info "Building pacman package for abp-studio ${PKGVER}-${PKGREL} (${ARCH})"
info "Running makepkg; this can take a while. Detailed output will be shown only if it fails"
if ! (
  cd "$BUILD_ROOT"
  PKGDEST="$DIST_DIR" makepkg -f --nodeps --skipinteg
) >"$MAKEPKG_LOG" 2>&1; then
  echo "[ERROR] makepkg failed. Last 80 log lines:" >&2
  tail -n 80 "$MAKEPKG_LOG" >&2 || true
  exit 1
fi

PACKAGE_FILE="$(find "$DIST_DIR" -name "abp-studio-${PKGVER}-*.pkg.tar.*" -print -quit)"
[ -n "$PACKAGE_FILE" ] || error "makepkg did not produce a package"
info "Built package: $PACKAGE_FILE"
