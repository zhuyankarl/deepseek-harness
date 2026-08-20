# Agent Note: dsh-manager Windows desktop launcher

Status: implemented

English | [中文](2026-08-21-dsh-manager-windows-launcher.zh.md)

## Problem

Developers running `dsh web` from source on Windows repeat the same manual sequence: pull updates, reinstall dependencies, rebuild, restart the service, and open the browser. The repository owns no helper for this, and ad-hoc local scripts drift out of sync with the build layout.

## Decision

Add [`tools/dsh-manager/`](../../../../tools/dsh-manager/README.md), a single-file C# WinForms launcher compiled by the in-box .NET Framework `csc.exe` — no SDK, no NuGet packages, no repository build involvement. It exposes three actions:

- **Update & Start**: `git fetch`, compare against `origin/master`, then on updates `git pull --ff-only` (fallback `--no-rebase`), `corepack pnpm install`, `build:lib`, the `@deepseek-ai/dsh-web-frontend` build, stop the running service, start it, poll the web port until it answers, and open the default browser. With no updates it still restarts the service.
- **Start**: reuse a running service and open the browser; otherwise start and wait for readiness. Never kills a running service.
- **Stop**: kill the matched node processes (`pnpm dsh web`, the `tsx` source-launch line) and the `cmd.exe` wrappers that own the log redirection.

Configuration is `dsh-manager.ini` next to the executable with `repo`, `port`, and `logdir` keys; the checkout path auto-detects by walking up to a `@deepseek-ai/dsh-root` `package.json` or by common locations. Logs default to `%LOCALAPPDATA%\dsh-manager\logs`. `DSHManager.exe --selftest` runs non-interactive checks (config round-trip, tool availability, matching patterns, repo detection, log-dir writability) as the tool's local evidence, since repository gates do not build it.

## Alternatives considered

- **PowerShell scripts.** Rejected: multiple files, execution-policy friction for double-click use, and no packaged selftest.
- **Committing the compiled executable.** Rejected: machine-specific binary artifact; `build.cmd` reproduces it with the in-box compiler.
- **A Node/tsx helper under `scripts/`.** Rejected: it needs the workspace's `node_modules`, which is exactly what may be broken when the service needs recovery.
- **Integrating into the `dsh` CLI.** Rejected: product surface should not grow for a developer convenience with a GUI-only workflow.

## Consequences

- Developers on Windows get one double-click flow for update/build/restart/browse.
- Process matching is heuristic and documented: any `src/bin.ts web` tsx launch matches. Stop actions kill the agent sessions hosted by the service.
- The tool is English-documented with a Chinese UI; it is maintained here as source plus `build.cmd` and stays out of every repository gate.
