# Repository Guidelines

## Project Structure & Module Organization

This repository packages ABP Studio for Linux without redistributing upstream
payloads. The .NET installer project lives in `src/AbpStudioLinux.Installer`,
organized by responsibility: `Cli`, `Installation`, `App`, `Upstream`,
`Packaging`, and `Infrastructure`. Unit tests live in
`tests/AbpStudioLinux.Installer.Tests`; most folders mirror production
responsibilities, repository-level shell checks live under `Repository`, and
shared helpers live under `Support`. Packaging automation is split between the
top-level `install.sh`, `Makefile` targets, `scripts/` wrappers and shell
helpers, and `packaging/linux/` templates and maintainer hooks. User-facing docs
and static project assets currently live at the repository root, including
`README.md`, `LICENSE.md`, and `abp-studio-on-fedora.png`.

Generated installer outputs live under `output/` (`output/abp-studio-app`,
`output/dist`, `output/work`, and `output/logs`) and should not be committed.
Native packages, proprietary upstream package archives, logs, coverage,
`TestResults/`, `bin/`, and `obj/` are also ignored or should be treated as
local-only artifacts.

## Build, Test, and Development Commands

- `dotnet build AbpStudioLinux.sln`: builds the .NET solution.
- `make build`: wraps the solution build.
- `dotnet test AbpStudioLinux.sln` or `make test`: runs the xUnit test suite.
- `make smoke`: syntax-checks `install.sh` and the shell scripts under
  `scripts/`.
- `make build-tool`: publishes the framework-dependent Linux x64 packaging
  tool to `output/dist/publish/`.
- `make build-app PKG=/path/abp-studio-<version>-stable-full.zip`: converts a
  local upstream archive into `output/abp-studio-app/`. The converter also
  handles the upstream `.nupkg`/package layouts used by the installer.
- `make deb`, `make rpm`, or `make pacman`: builds an explicit native package
  format.
- `make package FORMAT=deb|rpm|pacman`: builds the native package for a target
  package family; omit `FORMAT` to auto-detect from the current distro/tools.
- `make install FORMAT=deb|rpm|pacman`: installs the latest generated native
  package for the selected or detected package family.
- `make run-app`: runs the generated app from `output/abp-studio-app/`.
- `make clean`: removes generated output under `output/`.

## Coding Style & Naming Conventions

The project targets `net10.0` with nullable reference types, implicit usings,
and warnings as errors enabled in `Directory.Build.props`. Use 4-space
indentation for C#, file-scoped namespaces, PascalCase for public types and
methods, camelCase for locals and parameters, and `Async` suffixes for async
methods. Prefer `ProcessStartInfo.ArgumentList`, `ShellCommand`, and
`ProcessRunner` for process execution instead of shell-concatenated command
strings. Keep shell scripts Bash-compatible, quote variable expansions, and keep
them passing `bash -n` through `make smoke`.

## Testing Guidelines

Tests use xUnit. Place new tests under the matching responsibility folder in
`tests/AbpStudioLinux.Installer.Tests`, and use descriptive method names such as
`ParsesDotNetSdkVersionsFromListSdksOutput`. Prefer unit tests for parsers,
state handling, command builders, and package-format decisions. Use
`TestPaths.CreateTempDirectory()` for filesystem fixtures, keep tests
self-contained, and fake external commands/package managers instead of requiring
real ABP Studio payloads, root privileges, or a specific distro. The xUnit suite
also syntax-checks `packaging/linux/` maintainer hooks, so run
`dotnet test AbpStudioLinux.sln` and `make smoke` before opening a PR.

## Commit & Pull Request Guidelines

Recent history uses Conventional Commit prefixes, for example `feat:`, `docs:`,
and `chore:`. Keep commits focused and imperative. Pull requests should explain
the installer or packaging path affected, list validation commands run, mention
the tested distro/package format when relevant, and link related issues.

## Security & Configuration Tips

Do not commit proprietary ABP Studio `.pkg` files, generated native packages, or
extracted app content. Treat downloaded upstream `.nupkg`/`.zip` source archives
as proprietary payloads too. Treat installer and packaging changes carefully
because `install.sh`, `make install`, and native package maintainer hooks may
invoke `sudo`, package managers, desktop integration refreshes, and system
package hooks.
