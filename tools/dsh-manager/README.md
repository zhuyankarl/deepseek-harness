# dsh-manager

English | [中文](README.zh.md)

A Windows desktop launcher for the DeepSeek Harness `dsh web` server: update the checkout, rebuild it, restart the service, and open the GUI from one window. Single-file C# WinForms app with no third-party dependencies; it compiles with the in-box .NET Framework `csc.exe`, so no SDK or NuGet packages are required.

The UI strings are Chinese. The repository gates do not build or test this tool; `--selftest` is its local evidence.

## Buttons

| Button | Behavior |
|---|---|
| 更新并启动 (Update & Start) | `git fetch` → compare `HEAD..origin/master` → on updates: `git pull --ff-only` (fallback `--no-rebase`), `pnpm install`, `build:lib`, web frontend build → stop the running service → start it → poll `http://127.0.0.1:<port>/` until it answers → open the default browser. Without updates it still restarts the service. |
| 启动服务 (Start) | Reuse the running service and open the browser, or start it when stopped, then wait for readiness and open the browser. Never kills a running service. |
| 关闭服务 (Stop) | Stop the running `dsh web` processes (the `corepack pnpm dsh web` wrapper, the `node --import tsx/esm apps/cli/src/bin.ts web` server, and the `cmd.exe` wrappers that own log redirection). |

Commands prefer `corepack pnpm` and fall back to a bare `pnpm` when corepack is missing; the service is started with `--no-open` so the manager alone decides when to open the browser. The service runs detached, so closing the manager does not stop it.

## Configuration

`dsh-manager.ini` next to the executable, UTF-8, `key=value` lines:

```ini
# dsh-manager configuration
repo=C:\Users\me\source\repos\deepseek-harness
port=3080
logdir=
```

- `repo` — checkout path. When unset or invalid, the manager auto-detects: it walks up from the executable looking for a `package.json` named `@deepseek-ai/dsh-root`, then tries common locations (`%USERPROFILE%\source\repos\deepseek-harness`, `%USERPROFILE%\deepseek-harness`, `%USERPROFILE%\code\deepseek-harness`). A `…` button next to the path field picks any directory.
- `port` — the port the manager polls and opens in the browser. It must match the port the `dsh web` profile itself binds; the manager does not configure the server port.
- `logdir` — where `dsh-web-out.log` / `dsh-web-err.log` are written. Empty means `%LOCALAPPDATA%\dsh-manager\logs`.

## Build

Run `build.cmd` in this directory. It locates the in-box `csc.exe` and produces `DSHManager.exe` next to the source (gitignored). Copy the executable anywhere; the config file lives next to it.

## Self-test

`DSHManager.exe --selftest` runs non-interactive checks — config round-trip, tool availability (`git`, `node`, `corepack`, `csc`), process-matching patterns, repository auto-detection, log directory writability, and server reachability (informational) — and writes a report to the log directory. Exit code `0` means every gate passed.

## Known limitations

- Process matching is heuristic: a node process whose command line contains `pnpm dsh web`, or `src/bin.ts` together with a `web` argument, is treated as the dsh web server. An unrelated tsx project launched as `src/bin.ts web` would be matched too.
- Killing the service kills the agent sessions hosted by it; the manager deliberately does this only on 更新并启动 and 关闭服务.
- The update flow assumes the checkout layout of this repository (`build:lib` plus `@deepseek-ai/dsh-web-frontend`).
