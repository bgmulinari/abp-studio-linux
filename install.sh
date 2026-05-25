#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_ROOT="${OUTPUT_ROOT:-$SCRIPT_DIR/output}"
WORK_DIR="${WORK_DIR:-$OUTPUT_ROOT/work/install}"
DIST_DIR="${DIST_DIR:-$OUTPUT_ROOT/dist}"
APP_DIR="${APP_DIR:-$OUTPUT_ROOT/abp-studio-app}"
LOGS_DIR="${LOGS_DIR:-$OUTPUT_ROOT/logs}"
TOOL_BIN="$DIST_DIR/publish/abp-studio-linux-installer"
REQUIRED_DOTNET_SDK_MAJOR="${REQUIRED_DOTNET_SDK_MAJOR:-10}"
REQUIRED_NODE_VERSION="${REQUIRED_NODE_VERSION:-20.11.0}"

PKG_PATH=""
REQUESTED_VERSION=""
SKIP_DEPS=0
SKIP_INSTALL=0
FRESH=0
FORCE=0
ASSUME_YES=0
PACKAGE_FORMAT=""
RUNTIME_ROOT="${RUNTIME_ROOT:-}"

info() {
  echo -e "[INFO] $*" >&2
}

warn() {
  echo -e "[WARN] $*" >&2
}

error() {
  echo -e "[ERROR] $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || error "$1 is required"
}

run_sudo() {
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  else
    sudo "$@"
  fi
}

finish_install_log() {
  local status="$?"
  if [ "$status" -ne 0 ] && [ "${INSTALL_LOG_INTERRUPTED:-0}" != "1" ] && [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ] && [ -n "${INSTALL_LOG_FILE:-}" ]; then
    echo "[ERROR] Installation failed with exit code $status. Log saved to $INSTALL_LOG_FILE" >&2
  fi
  if [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ]; then
    exec 1>&3 2>&4
    exec 3>&- 4>&-
    wait "$INSTALL_LOG_STDOUT_TEE_PID" "$INSTALL_LOG_STDERR_TEE_PID" 2>/dev/null || true
  fi
}

interrupt_install() {
  local signal_name="$1"
  local status="$2"
  trap - INT TERM
  export INSTALL_LOG_INTERRUPTED=1

  echo "" >&2
  if [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ] && [ -n "${INSTALL_LOG_FILE:-}" ]; then
    echo "[ERROR] Installation interrupted by $signal_name. Log saved to $INSTALL_LOG_FILE" >&2
  else
    echo "[ERROR] Installation interrupted by $signal_name" >&2
  fi
  exit "$status"
}

start_install_log() {
  case "${INSTALL_LOG_ENABLED:-1}" in
    0|false|FALSE|False|no|NO|No)
      return 0
      ;;
  esac

  [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ] && return 0

  local log_file log_parent stderr_pipe stdout_pipe timestamp
  if [ -n "${INSTALL_LOG_FILE:-}" ]; then
    log_file="$INSTALL_LOG_FILE"
  else
    timestamp="$(date '+%Y%m%d-%H%M%S')"
    log_file="$LOGS_DIR/install-$timestamp-$$.log"
  fi
  log_parent="$(dirname "$log_file")"

  if ! mkdir -p "$log_parent" 2>/dev/null; then
    case "$OUTPUT_ROOT" in
      "$SCRIPT_DIR/output")
        if [ -e "$OUTPUT_ROOT" ]; then
          warn "Repairing ownership of generated output directory: $OUTPUT_ROOT"
          if ! run_sudo chown -R "$(id -u):$(id -g)" "$OUTPUT_ROOT"; then
            error "Unable to repair generated output directory ownership: $OUTPUT_ROOT"
          fi
        fi
        mkdir -p "$log_parent" || error "Unable to create installer log directory: $log_parent"
        ;;
      *)
        error "Unable to create installer log directory: $log_parent. Fix its ownership or set OUTPUT_ROOT to a writable directory."
        ;;
    esac
  fi

  export INSTALL_LOG_FILE="$log_file"

  # Explicit FIFOs let the exit trap wait for tee, including very short runs.
  exec 3>&1 4>&2
  INSTALL_LOG_PIPE_DIR="$(mktemp -d "$log_parent/.install-log-pipes.XXXXXXXXXX")"
  stdout_pipe="$INSTALL_LOG_PIPE_DIR/stdout"
  stderr_pipe="$INSTALL_LOG_PIPE_DIR/stderr"
  mkfifo "$stdout_pipe" "$stderr_pipe"

  tee -a "$INSTALL_LOG_FILE" < "$stdout_pipe" >&3 &
  INSTALL_LOG_STDOUT_TEE_PID="$!"
  tee -a "$INSTALL_LOG_FILE" < "$stderr_pipe" >&4 &
  INSTALL_LOG_STDERR_TEE_PID="$!"

  exec 1>"$stdout_pipe" 2>"$stderr_pipe"
  rm -f "$stdout_pipe" "$stderr_pipe"
  rmdir "$INSTALL_LOG_PIPE_DIR" 2>/dev/null || true
  export INSTALL_LOG_ACTIVE=1
  export INSTALL_LOG_ANNOUNCED=0
}

announce_install_log() {
  if [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ] && [ "${INSTALL_LOG_ANNOUNCED:-0}" = "0" ]; then
    export INSTALL_LOG_ANNOUNCED=1
    info "Writing installation log to $INSTALL_LOG_FILE"
  fi
}

detect_package_manager() {
  if command -v dnf >/dev/null 2>&1; then
    echo "dnf"
  elif command -v apt-get >/dev/null 2>&1; then
    echo "apt-get"
  elif command -v pacman >/dev/null 2>&1; then
    echo "pacman"
  elif command -v zypper >/dev/null 2>&1; then
    echo "zypper"
  else
    echo "unknown"
  fi
}

detect_shell() {
  local shell_version=""
  if [ -n "${SHELL:-}" ] && [ -x "$SHELL" ]; then
    shell_version="$("$SHELL" --version 2>/dev/null | sed -n '1p' || true)"
  fi

  if [ -n "$shell_version" ]; then
    echo "$shell_version"
  else
    echo "${SHELL:-unknown}"
  fi
}

detect_repository_head() {
  command -v git >/dev/null 2>&1 || return 0
  git -C "$SCRIPT_DIR" rev-parse HEAD 2>/dev/null || true
}

print_startup_diagnostics() {
  local distro="unknown"
  local kernel
  local os_id="unknown"
  local os_version="unknown"
  local repository_head
  if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    distro="${PRETTY_NAME:-${NAME:-unknown}}"
    os_id="${ID:-unknown}"
    os_version="${VERSION_ID:-unknown}"
  fi
  kernel="$(uname -sr 2>/dev/null || uname -s)"
  repository_head="$(detect_repository_head)"

  info "Startup diagnostics"
  info "  Distro: $distro ($os_id $os_version)"
  info "  Kernel: $kernel"
  info "  Shell: $(detect_shell)"
  info "  Package manager: $(detect_package_manager)"
  info "  .NET: $(command -v dotnet 2>/dev/null || echo missing)"
  info "  Node.js: $(command -v node 2>/dev/null || echo missing)"
  if [ -n "$repository_head" ]; then
    info "  Repository HEAD: $repository_head"
  fi
  info "  Options: skip_deps=$SKIP_DEPS no_install=$SKIP_INSTALL fresh=$FRESH force=$FORCE yes=$ASSUME_YES version=${REQUESTED_VERSION:-latest} format=${PACKAGE_FORMAT:-auto} pkg=${PKG_PATH:-auto} runtime_root=${RUNTIME_ROOT:-auto}"
}

confirm_installation() {
  if [ "$ASSUME_YES" -eq 1 ]; then
    return
  fi

  local prompt
  local reply=""
  if [ "$SKIP_DEPS" -eq 0 ] && [ "$SKIP_INSTALL" -eq 0 ]; then
    prompt="Proceed with installing dependencies, building the package, and installing ABP Studio?"
  elif [ "$SKIP_DEPS" -eq 0 ]; then
    prompt="Proceed with installing dependencies and building the ABP Studio package?"
  elif [ "$SKIP_INSTALL" -eq 0 ]; then
    prompt="Proceed with building the package and installing ABP Studio?"
  else
    prompt="Proceed with building the ABP Studio package?"
  fi

  echo "" >&2
  printf "%s [y/N] " "$prompt" >&2
  if ! read -r reply; then
    echo "" >&2
    info "Installation cancelled"
    exit 0
  fi
  echo "" >&2

  case "$reply" in
    y|Y|yes|YES|Yes)
      ;;
    *)
      info "Installation cancelled"
      exit 0
      ;;
  esac
}

format_duration() {
  local total_seconds="$1"
  local hours=$((total_seconds / 3600))
  local minutes=$(((total_seconds % 3600) / 60))
  local seconds=$((total_seconds % 60))

  if [ "$hours" -gt 0 ]; then
    printf "%dh %dm %ds" "$hours" "$minutes" "$seconds"
  elif [ "$minutes" -gt 0 ]; then
    printf "%dm %ds" "$minutes" "$seconds"
  else
    printf "%ds" "$seconds"
  fi
}

shell_quote_command() {
  printf "%q" "$1"
  shift
  for arg in "$@"; do
    printf " %q" "$arg"
  done
}

usage() {
  cat <<'USAGE'
Unofficial ABP Studio Linux installer
https://github.com/bgmulinari/abp-studio-linux

Usage:
  ./install.sh [options]

Options:
  --version VERSION    Install a specific ABP Studio/CLI version instead of latest
  --format FORMAT      Build deb, rpm, or pacman instead of auto-detecting
  --runtime-root PATH  Use an explicit .NET root to bundle under /opt/abp-studio
  --skip-deps          Do not install system package dependencies
  --no-install         Build the native package but do not install it
  --fresh              Remove generated output directories before starting
  --force              Reinstall even when the target ABP Studio version is installed
  -y, --yes            Confirm dependency and native package installs automatically
  -h, --help           Show this help

Default behavior:
  check for .NET SDK and Node.js, install Linux helper/package dependencies,
  build the packaging tool, install or update the ABP CLI, use its installed
  version or --version to download ABP Studio, convert it, build a native
  package, and hand it to the native package manager.
USAGE
}

start_install_log
trap finish_install_log EXIT
trap 'interrupt_install SIGINT 130' INT
trap 'interrupt_install SIGTERM 143' TERM

while [ "$#" -gt 0 ]; do
  case "$1" in
    --version)
      [ "$#" -ge 2 ] || error "--version requires a version"
      REQUESTED_VERSION="$2"
      case "$REQUESTED_VERSION" in
        *[![:space:]]*) ;;
        *) error "--version requires a non-empty version" ;;
      esac
      shift 2
      ;;
    --format)
      [ "$#" -ge 2 ] || error "--format requires deb, rpm, or pacman"
      PACKAGE_FORMAT="$2"
      shift 2
      ;;
    --runtime-root)
      [ "$#" -ge 2 ] || error "--runtime-root requires a path"
      RUNTIME_ROOT="$2"
      shift 2
      ;;
    --skip-deps)
      SKIP_DEPS=1
      shift
      ;;
    --no-install)
      SKIP_INSTALL=1
      shift
      ;;
    --fresh)
      FRESH=1
      shift
      ;;
    -y|--yes)
      ASSUME_YES=1
      shift
      ;;
    --force)
      FORCE=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      error "Unknown option: $1"
      ;;
  esac
done

ensure_generated_output_writable() {
  [ -e "$OUTPUT_ROOT" ] || return 0

  if [ -w "$OUTPUT_ROOT" ] && { [ ! -e "$DIST_DIR" ] || [ -w "$DIST_DIR" ]; }; then
    return 0
  fi

  case "$OUTPUT_ROOT" in
    "$SCRIPT_DIR/output")
      warn "Repairing ownership of generated output directory: $OUTPUT_ROOT"
      run_sudo chown -R "$(id -u):$(id -g)" "$OUTPUT_ROOT"
      ;;
    *)
      error "Generated output directory is not writable: $OUTPUT_ROOT. Fix its ownership or set OUTPUT_ROOT to a writable directory."
      ;;
  esac
}

install_dependencies() {
  if [ "$SKIP_DEPS" -eq 1 ]; then
    info "Skipping dependency installation"
    return
  fi

  info "Checking dependency installation"
  if command -v dnf >/dev/null 2>&1; then
    local dnf_args=(install)
    [ "$ASSUME_YES" -eq 1 ] && dnf_args+=(-y)
    run_sudo dnf "${dnf_args[@]}" git make curl ca-certificates bsdtar libicu rpm-build mkcert wireguard-tools
  elif command -v apt-get >/dev/null 2>&1; then
    run_sudo apt-get update
    local apt_args=(install --no-install-recommends)
    [ "$ASSUME_YES" -eq 1 ] && apt_args+=(-y)
    run_sudo apt-get "${apt_args[@]}" git make curl ca-certificates libarchive-tools libicu-dev dpkg-dev mkcert wireguard-tools
  elif command -v pacman >/dev/null 2>&1; then
    local pacman_args=(-Syu --needed)
    [ "$ASSUME_YES" -eq 1 ] && pacman_args+=(--noconfirm)
    run_sudo pacman "${pacman_args[@]}" git make curl ca-certificates libarchive icu base-devel mkcert wireguard-tools
  elif command -v zypper >/dev/null 2>&1; then
    local zypper_args=()
    [ "$ASSUME_YES" -eq 1 ] && zypper_args+=(--non-interactive)
    zypper_args+=(install)
    run_sudo zypper "${zypper_args[@]}" git make curl ca-certificates bsdtar libicu-devel rpm-build mkcert wireguard-tools
  else
    error "No supported package manager found. Install the required dependencies manually or rerun with --skip-deps."
  fi
}

ensure_architecture() {
  case "$(uname -m)" in
    x86_64) ;;
    *) error "Only x86_64 is supported" ;;
  esac
}

version_at_least() {
  local actual="${1#v}"
  local required="${2#v}"
  local actual_major actual_minor actual_patch required_major required_minor required_patch
  IFS=. read -r actual_major actual_minor actual_patch <<< "$actual"
  IFS=. read -r required_major required_minor required_patch <<< "$required"
  actual_minor="${actual_minor:-0}"
  actual_patch="${actual_patch:-0}"
  required_minor="${required_minor:-0}"
  required_patch="${required_patch:-0}"

  if [ "$actual_major" -gt "$required_major" ]; then return 0; fi
  if [ "$actual_major" -lt "$required_major" ]; then return 1; fi
  if [ "$actual_minor" -gt "$required_minor" ]; then return 0; fi
  if [ "$actual_minor" -lt "$required_minor" ]; then return 1; fi
  [ "$actual_patch" -ge "$required_patch" ]
}

ensure_user_prerequisites() {
  command -v dotnet >/dev/null 2>&1 || error ".NET SDK $REQUIRED_DOTNET_SDK_MAJOR.x is required. Make sure it is installed and available in PATH (both dotnet CLI and tools), then re-run ./install.sh.\nFor more information: https://learn.microsoft.com/en-us/dotnet/core/install/linux"
  DOTNET_CMD="$(command -v dotnet)"
  export DOTNET_CMD

  local dotnet_sdks dotnet_sdk candidate
  dotnet_sdks="$("$DOTNET_CMD" --list-sdks 2>/dev/null || true)"
  dotnet_sdk=""
  while IFS=' [' read -r candidate _; do
    case "$candidate" in
      "${REQUIRED_DOTNET_SDK_MAJOR}."*)
        if [ -z "$dotnet_sdk" ] || version_at_least "$candidate" "$dotnet_sdk"; then
          dotnet_sdk="$candidate"
        fi
        ;;
    esac
  done <<< "$dotnet_sdks"
  [ -n "$dotnet_sdk" ] || error ".NET SDK $REQUIRED_DOTNET_SDK_MAJOR.x is required. Installed SDKs: $dotnet_sdks.\nFor more information: https://learn.microsoft.com/en-us/dotnet/core/install/linux"
  info "Using .NET SDK: $dotnet_sdk"

  command -v node >/dev/null 2>&1 || error "Node.js $REQUIRED_NODE_VERSION or newer is required. Install it, then re-run ./install.sh.\nFor more information: https://nodejs.org/en/download"
  NODE_CMD="$(command -v node)"
  export NODE_CMD

  local node_version
  node_version="$("$NODE_CMD" --version)"
  version_at_least "$node_version" "$REQUIRED_NODE_VERSION" || error "Node.js $REQUIRED_NODE_VERSION or newer is required. Found $node_version.\nFor more information: https://nodejs.org/en/download"
  info "Using Node.js: $node_version"
}

ensure_linux_helper_tools() {
  require_command mkcert
  require_command wg
}

publish_packaging_tool() {
  info "Building packaging tool"
  make -C "$SCRIPT_DIR" build-tool
  [ -x "$TOOL_BIN" ] || error "Failed to publish $TOOL_BIN"
}

run_managed_installer() {
  local args=(
    install
    --repo-root "$SCRIPT_DIR"
    --work-dir "$WORK_DIR"
    --dist-dir "$DIST_DIR"
    --app-dir "$APP_DIR"
    --dotnet "$DOTNET_CMD"
    --node "$NODE_CMD"
  )

  if [ -n "$REQUESTED_VERSION" ]; then
    args+=(--version "$REQUESTED_VERSION")
  fi
  if [ -n "$PACKAGE_FORMAT" ]; then
    args+=(--format "$PACKAGE_FORMAT")
  fi
  if [ -n "$RUNTIME_ROOT" ]; then
    args+=(--runtime-root "$RUNTIME_ROOT")
  fi
  if [ "$SKIP_INSTALL" -eq 1 ]; then
    args+=(--no-install)
  fi
  if [ "$ASSUME_YES" -eq 1 ]; then
    args+=(--yes)
  fi
  if [ "$FORCE" -eq 1 ]; then
    args+=(--force)
  fi

  info "Running managed installer: $(shell_quote_command "$TOOL_BIN" "${args[@]}")"
  "$TOOL_BIN" "${args[@]}"
}

main() {
  echo "╔════════════════════════════════════════════════════╗" >&2
  echo "║    Unofficial installer for ABP Studio on Linux    ║" >&2
  echo "║   https://github.com/bgmulinari/abp-studio-linux   ║" >&2
  echo "╚════════════════════════════════════════════════════╝" >&2
  echo "" >&2
  announce_install_log
  print_startup_diagnostics

  ensure_architecture
  ensure_user_prerequisites
  confirm_installation
  SECONDS=0

  ensure_generated_output_writable
  if [ "$FRESH" -eq 1 ]; then
    info "Removing generated output directories"
    rm -rf "$APP_DIR" "$WORK_DIR" "$DIST_DIR"
  fi

  mkdir -p "$WORK_DIR" "$DIST_DIR"
  install_dependencies
  require_command curl
  require_command make
  require_command bsdtar
  ensure_linux_helper_tools
  publish_packaging_tool
  run_managed_installer

  info "ABP Studio package flow completed in $(format_duration "$SECONDS")"
  echo "" >&2
  echo "══════════════════════════════════════════════════════" >&2
  if [ "$SKIP_INSTALL" -eq 0 ]; then
    echo "  Run the abp-studio command or open ABP Studio from your application launcher" >&2
  else
    echo "  Package output: $DIST_DIR" >&2
  fi
  if [ "${INSTALL_LOG_ACTIVE:-0}" = "1" ]; then
    echo "" >&2
    echo "  Installation log: $INSTALL_LOG_FILE" >&2
  fi
  echo "══════════════════════════════════════════════════════" >&2
}

main "$@"
