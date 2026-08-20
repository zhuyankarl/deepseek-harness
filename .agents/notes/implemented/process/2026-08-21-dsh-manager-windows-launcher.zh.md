# Agent Note: dsh-manager Windows 桌面启动器

Status: implemented

[English](2026-08-21-dsh-manager-windows-launcher.md) | 中文

## 问题

在 Windows 上从源码运行 `dsh web` 的开发者需要反复执行同一套手动流程：拉取更新、重装依赖、重新构建、重启服务、打开浏览器。仓库没有提供任何辅助工具，而临时的本地脚本会逐渐与构建布局脱节。

## 决策

新增 [`tools/dsh-manager/`](../../../../tools/dsh-manager/README.md)，一个单文件 C# WinForms 启动器，使用系统自带的 .NET Framework `csc.exe` 编译——无需 SDK、无需 NuGet 包、不参与仓库构建。它提供三个操作：

- **更新并启动**：`git fetch`，与 `origin/master` 对比，有更新时依次执行 `git pull --ff-only`（失败回退 `--no-rebase`）、`corepack pnpm install`、`build:lib`、`@deepseek-ai/dsh-web-frontend` 构建，停止正在运行的服务，启动新服务，轮询 Web 端口直到可访问，然后打开默认浏览器。无更新时也会重启服务。
- **启动**：服务已在运行则直接打开浏览器；否则启动并等待就绪。绝不会关闭正在运行的服务。
- **关闭**：结束匹配到的 node 进程（`pnpm dsh web`、`tsx` 源码启动命令行）以及持有日志重定向的 `cmd.exe` 包装进程。

配置为可执行文件旁的 `dsh-manager.ini`，包含 `repo`、`port`、`logdir` 三个键；仓库路径自动探测——向上逐级查找名为 `@deepseek-ai/dsh-root` 的 `package.json`，再尝试常见位置。日志默认写入 `%LOCALAPPDATA%\dsh-manager\logs`。`DSHManager.exe --selftest` 运行非交互检查（配置读写回环、工具可用性、匹配规则、仓库探测、日志目录可写性），作为该工具的本地验证证据，因为仓库门禁不构建它。

## 考虑过的替代方案

- **PowerShell 脚本。** 不采用：文件多、双击使用时受执行策略阻碍，且没有内置自检。
- **提交编译后的可执行文件。** 不采用：机器相关的二进制产物；`build.cmd` 用系统自带编译器即可复现。
- **放在 `scripts/` 下的 Node/tsx 助手。** 不采用：它依赖工作区的 `node_modules`，而服务需要恢复时恰恰可能是它先坏掉。
- **集成进 `dsh` CLI。** 不采用：不应为一个纯 GUI 工作流的开发便利扩展产品面。

## 后果

- Windows 开发者获得一条龙双击流程：更新/构建/重启/打开页面。
- 进程匹配是启发式的且已记录在案：任何以 `src/bin.ts web` 方式启动的 tsx 项目都会被匹配。停止操作会终止服务所承载的 agent 会话。
- 该工具文档为英文、界面为中文；以源码加 `build.cmd` 的形式在此维护，不进入任何仓库门禁。
