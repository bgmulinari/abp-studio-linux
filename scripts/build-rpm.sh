#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
. "$REPO_DIR/scripts/lib/common.sh"

require_command rpmbuild

OUTPUT_ROOT="${OUTPUT_ROOT:-$REPO_DIR/output}"
APP_DIR="${APP_DIR:-$OUTPUT_ROOT/abp-studio-app}"
DIST_DIR="${DIST_DIR:-$OUTPUT_ROOT/dist}"
WORK_DIR="${WORK_DIR:-$OUTPUT_ROOT/work}"
PACKAGE_VERSION="${PACKAGE_VERSION:-$(detect_app_version "$APP_DIR")}"
ARCH="$(map_rpm_arch)"
RPM_VERSION="${PACKAGE_VERSION%%+*}"
RPM_RELEASE="1"
if [ "$RPM_VERSION" != "$PACKAGE_VERSION" ]; then
  RPM_RELEASE="${PACKAGE_VERSION#*+}"
fi

detect_rpm_build_ncpus() {
  local value
  value="${RPM_BUILD_NCPUS:-}"
  if [ -z "$value" ]; then
    value="$(rpm --eval '%{getncpus thread}' 2>/dev/null || true)"
  fi
  if ! [[ "$value" =~ ^[0-9]+$ ]] || [ "$value" -lt 1 ]; then
    if command -v nproc >/dev/null 2>&1; then
      value="$(nproc)"
    else
      value="1"
    fi
  fi

  echo "$value"
}

validate_non_negative_integer() {
  local name="$1"
  local value="$2"
  [[ "$value" =~ ^[0-9]+$ ]] || error "$name must be a non-negative integer, got: $value"
}

RPM_BUILD_NCPUS="$(detect_rpm_build_ncpus)"
validate_non_negative_integer "RPM_BUILD_NCPUS" "$RPM_BUILD_NCPUS"
[ "$RPM_BUILD_NCPUS" -gt 0 ] || error "RPM_BUILD_NCPUS must be greater than zero"

RPM_ZSTD_THREADS="${RPM_ZSTD_THREADS:-$RPM_BUILD_NCPUS}"
RPM_ZSTD_LEVEL="${RPM_ZSTD_LEVEL:-19}"
validate_non_negative_integer "RPM_ZSTD_THREADS" "$RPM_ZSTD_THREADS"
validate_non_negative_integer "RPM_ZSTD_LEVEL" "$RPM_ZSTD_LEVEL"
[ "$RPM_ZSTD_LEVEL" -gt 0 ] || error "RPM_ZSTD_LEVEL must be greater than zero"
RPM_BINARY_PAYLOAD="${RPM_BINARY_PAYLOAD:-w${RPM_ZSTD_LEVEL}T${RPM_ZSTD_THREADS}.zstdio}"

mkdir -p "$WORK_DIR"
BUILD_ROOT="$(mktemp -d "$WORK_DIR/rpm-build.XXXXXXXXXX")"
trap 'rm -rf "$BUILD_ROOT"' EXIT
STAGING_ROOT="$BUILD_ROOT/staging"
SPEC_FILE="$BUILD_ROOT/abp-studio.spec"
RPMBUILD_ROOT="$BUILD_ROOT/rpmbuild"
RPMBUILD_LOG="$BUILD_ROOT/rpmbuild.log"

run_tool stage-package \
  --format rpm \
  --repo-root "$REPO_DIR" \
  --app-dir "$APP_DIR" \
  --staging-root "$STAGING_ROOT"

mkdir -p "$RPMBUILD_ROOT/RPMS" "$RPMBUILD_ROOT/SRPMS" "$RPMBUILD_ROOT/BUILD" "$RPMBUILD_ROOT/SOURCES" "$DIST_DIR" "$BUILD_ROOT/tmp"
sed \
  -e "s/__RPM_VERSION__/$(sed_escape "$RPM_VERSION")/g" \
  -e "s/__RPM_RELEASE__/$(sed_escape "$RPM_RELEASE")/g" \
  -e "s/__ARCH__/$(sed_escape "$ARCH")/g" \
  -e "s/__STAGING_ROOT__/$(sed_escape "$STAGING_ROOT")/g" \
  "$REPO_DIR/packaging/linux/abp-studio.spec.template" > "$SPEC_FILE"

info "Building abp-studio-${RPM_VERSION}-${RPM_RELEASE}.${ARCH}.rpm"
info "Using RPM build CPUs: $RPM_BUILD_NCPUS"
info "Using RPM binary payload: $RPM_BINARY_PAYLOAD"
info "Running rpmbuild; this can take a while. Detailed output will be shown only if it fails"
if ! rpmbuild -bb \
  --define "_rpmdir $RPMBUILD_ROOT/RPMS" \
  --define "_srcrpmdir $RPMBUILD_ROOT/SRPMS" \
  --define "_builddir $RPMBUILD_ROOT/BUILD" \
  --define "_sourcedir $RPMBUILD_ROOT/SOURCES" \
  --define "_specdir $BUILD_ROOT" \
  --define "_tmppath $BUILD_ROOT/tmp" \
  --define "_smp_build_ncpus $RPM_BUILD_NCPUS" \
  --define "_binary_payload $RPM_BINARY_PAYLOAD" \
  --define "_build_name_fmt %%{NAME}-%%{VERSION}-%%{RELEASE}.%%{ARCH}.rpm" \
  "$SPEC_FILE" >"$RPMBUILD_LOG" 2>&1; then
  echo "[ERROR] rpmbuild failed. Last 80 log lines:" >&2
  tail -n 80 "$RPMBUILD_LOG" >&2 || true
  exit 1
fi

RPM_FILE="$(find "$RPMBUILD_ROOT/RPMS" -name '*.rpm' -print -quit)"
[ -n "$RPM_FILE" ] || error "rpmbuild did not produce an RPM"
OUTPUT="$DIST_DIR/abp-studio-${RPM_VERSION}-${RPM_RELEASE}.${ARCH}.rpm"
cp "$RPM_FILE" "$OUTPUT"
info "Built package: $OUTPUT"
