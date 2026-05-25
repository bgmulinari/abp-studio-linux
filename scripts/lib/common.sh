#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
TOOL_PROJECT="$REPO_DIR/src/AbpStudioLinux.Installer/AbpStudioLinux.Installer.csproj"
OUTPUT_ROOT="${OUTPUT_ROOT:-$REPO_DIR/output}"
PUBLISHED_TOOL="${ABP_STUDIO_LINUX_TOOL:-$OUTPUT_ROOT/dist/publish/abp-studio-linux-installer}"

info() {
  echo "[INFO] $*" >&2
}

error() {
  echo "[ERROR] $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || error "$1 is required"
}

run_tool() {
  if [ -x "$PUBLISHED_TOOL" ]; then
    "$PUBLISHED_TOOL" "$@"
  else
    dotnet run --project "$TOOL_PROJECT" -- "$@"
  fi
}

detect_app_version() {
  run_tool app-version --app-dir "$1"
}

sed_escape() {
  printf '%s' "$1" | sed -e 's/[\/&]/\\&/g'
}

map_deb_arch() {
  case "$(dpkg --print-architecture)" in
    amd64|arm64|armhf) dpkg --print-architecture ;;
    *) error "Unsupported Debian architecture: $(dpkg --print-architecture)" ;;
  esac
}

map_rpm_arch() {
  case "$(uname -m)" in
    x86_64) echo "x86_64" ;;
    aarch64) echo "aarch64" ;;
    *) error "Unsupported RPM architecture: $(uname -m)" ;;
  esac
}

map_pacman_arch() {
  case "$(uname -m)" in
    x86_64) echo "x86_64" ;;
    aarch64) echo "aarch64" ;;
    *) error "Unsupported pacman architecture: $(uname -m)" ;;
  esac
}
