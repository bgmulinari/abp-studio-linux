SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

OUTPUT_ROOT ?= $(CURDIR)/output
APP_DIR ?= $(OUTPUT_ROOT)/abp-studio-app
PKG ?=
DIST_DIR ?= $(OUTPUT_ROOT)/dist
WORK_DIR ?= $(OUTPUT_ROOT)/work/build-app
TOOL_OUT ?= $(DIST_DIR)/publish
FORMAT ?=
TOOL_BIN := $(TOOL_OUT)/abp-studio-linux-installer
DEB_GLOB := $(DIST_DIR)/abp-studio_*.deb
RPM_GLOB := $(DIST_DIR)/abp-studio-*.rpm
PACMAN_GLOB := $(DIST_DIR)/abp-studio-[0-9]*.pkg.tar.*

.DEFAULT_GOAL := help

.PHONY: help build test smoke build-tool build-app deb rpm pacman package install run-app clean

help:
	@printf '\nABP Studio Linux targets\n\n'
	@printf '  %-18s %s\n' "make build" "Build the .NET solution"
	@printf '  %-18s %s\n' "make test" "Run unit tests"
	@printf '  %-18s %s\n' "make smoke" "Run shell syntax smoke checks"
	@printf '  %-18s %s\n' "make build-tool" "Publish framework-dependent linux-x64 packaging tool"
	@printf '  %-18s %s\n' "make build-app" "Build output/abp-studio-app from PKG=/path/file.pkg"
	@printf '  %-18s %s\n' "make deb" "Build Debian package"
	@printf '  %-18s %s\n' "make rpm" "Build RPM package"
	@printf '  %-18s %s\n' "make pacman" "Build Arch pacman package"
	@printf '  %-18s %s\n' "make package" "Build native package for this distro"
	@printf '  %-18s %s\n' "make install" "Install latest native package for this distro"
	@printf '  %-18s %s\n' "make run-app" "Run generated app"
	@printf '\nExample:\n  make build-app PKG=/path/abp-studio-stable-Setup.pkg\n\n'

build:
	dotnet build AbpStudioLinux.sln

test:
	dotnet test AbpStudioLinux.sln

smoke:
	bash -n install.sh
	bash -n scripts/lib/common.sh
	bash -n scripts/build-app.sh
	bash -n scripts/build-deb.sh
	bash -n scripts/build-rpm.sh
	bash -n scripts/build-pacman.sh

build-tool:
	rm -rf "$(TOOL_OUT)"
	dotnet publish src/AbpStudioLinux.Installer/AbpStudioLinux.Installer.csproj -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=portable -p:DebugSymbols=true -o "$(TOOL_OUT)"

build-app:
	PKG="$(PKG)" APP_DIR="$(APP_DIR)" WORK_DIR="$(WORK_DIR)" NATIVE_OVERRIDES="$(NATIVE_OVERRIDES)" RUNTIME_ROOT="$(RUNTIME_ROOT)" FIXTURE_PAYLOAD_DIR="$(FIXTURE_PAYLOAD_DIR)" ./scripts/build-app.sh

deb: build-tool
	APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-deb.sh

rpm: build-tool
	APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-rpm.sh

pacman: build-tool
	APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-pacman.sh

package: build-tool
	@format="$(FORMAT)"; \
	if [ -r /etc/os-release ]; then \
	  if [ -z "$$format" ]; then \
	    . /etc/os-release; \
	    for token in $${ID:-} $${ID_LIKE:-}; do \
	      case "$$token" in \
	        arch|archlinux|manjaro) format="pacman" ;; \
	        fedora|rhel|centos|rocky|almalinux|suse|opensuse) format="rpm" ;; \
	        debian|ubuntu|linuxmint|pop) format="deb" ;; \
	      esac; \
	    done; \
	  fi; \
	fi; \
	if [ -z "$$format" ] && command -v dpkg-deb >/dev/null 2>&1; then format="deb"; fi; \
	if [ -z "$$format" ] && command -v rpmbuild >/dev/null 2>&1; then format="rpm"; fi; \
	if [ -z "$$format" ] && command -v makepkg >/dev/null 2>&1; then format="pacman"; fi; \
	case "$$format" in \
	  deb) APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-deb.sh ;; \
	  rpm) APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-rpm.sh ;; \
	  pacman) APP_DIR="$(APP_DIR)" DIST_DIR="$(DIST_DIR)" ./scripts/build-pacman.sh ;; \
	  *) echo "No supported native package builder found" >&2; exit 1 ;; \
	esac

install:
	@format="$(FORMAT)"; \
	if [ -r /etc/os-release ]; then \
	  if [ -z "$$format" ]; then \
	    . /etc/os-release; \
	    for token in $${ID:-} $${ID_LIKE:-}; do \
	      case "$$token" in \
	        arch|archlinux|manjaro) format="pacman" ;; \
	        fedora|rhel|centos|rocky|almalinux|suse|opensuse) format="rpm" ;; \
	        debian|ubuntu|linuxmint|pop) format="deb" ;; \
	      esac; \
	    done; \
	  fi; \
	fi; \
	if [ "$$format" = "deb" ]; then \
	  deb="$$(ls -1 $(DEB_GLOB) 2>/dev/null | sort -V | tail -n 1)"; \
	  [ -n "$$deb" ] || { echo "No Debian package found" >&2; exit 1; }; \
	  if command -v apt-get >/dev/null 2>&1; then \
	    tmpdir="$$(mktemp -d)"; \
	    cp "$$deb" "$$tmpdir/"; \
	    chmod 0644 "$$tmpdir/$$(basename "$$deb")"; \
	    sudo apt-get install -y "$$tmpdir/$$(basename "$$deb")"; \
	    status="$$?"; \
	    rm -rf "$$tmpdir"; \
	    exit "$$status"; \
	  else sudo dpkg -i "$$deb"; fi; \
	elif [ "$$format" = "rpm" ]; then \
	  rpm="$$(ls -1 $(RPM_GLOB) 2>/dev/null | sort -V | tail -n 1)"; \
	  [ -n "$$rpm" ] || { echo "No RPM package found" >&2; exit 1; }; \
	  if command -v dnf >/dev/null 2>&1; then sudo dnf install -y "$$rpm"; else sudo rpm -Uvh "$$rpm"; fi; \
	elif [ "$$format" = "pacman" ]; then \
	  pkg="$$(ls -1 $(PACMAN_GLOB) 2>/dev/null | sort -V | tail -n 1)"; \
	  [ -n "$$pkg" ] || { echo "No pacman package found" >&2; exit 1; }; \
	  sudo pacman -U --noconfirm "$$pkg"; \
	else \
	  echo "Could not detect native package manager" >&2; exit 1; \
	fi

run-app:
	"$(APP_DIR)/start.sh"

clean:
	rm -rf "$(OUTPUT_ROOT)"
