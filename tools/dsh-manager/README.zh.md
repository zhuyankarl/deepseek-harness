# dsh-manager

[English](README.md) | 中文

DeepSeek Harness `dsh web` 服务的 Windows 桌面启动器：在一个窗口里完成“更新代码、重新构建、重启服务、打开页面”。单文件 C# WinForms 应用，无任何第三方依赖；使用系统自带的 .NET Framework `csc.exe` 编译，无需 SDK 或 NuGet 包。

界面文案为中文。仓库门禁不构建、不测试该工具；`--selftest` 是它的本地验证手段。

## 按钮

| 按钮 | 行为 |
|---|---|
| 更新并启动 | `git fetch` → 对比 `HEAD..origin/master` → 有更新时：`git pull --ff-only`（失败则 `--no-rebase`）、`pnpm install`、`build:lib`、Web 前端构建 → 停止正在运行的服务 → 启动 → 轮询 `http://127.0.0.1:<port>/` 直到可访问 → 打开默认浏览器。无更新时也会重启服务。 |
| 启动服务 | 服务已在运行则直接打开浏览器；未运行则启动，等待就绪后打开浏览器。绝不会关闭正在运行的服务。 |
| 关闭服务 | 停止正在运行的 `dsh web` 进程（`corepack pnpm dsh web` 包装进程、`node --import tsx/esm apps/cli/src/bin.ts web` 服务进程，以及持有日志重定向的 `cmd.exe` 包装进程）。 |

命令优先使用 `corepack pnpm`，corepack 不可用时回退到裸 `pnpm`。服务以独立进程启动，关闭管理器不影响服务。

## 配置

可执行文件旁的 `dsh-manager.ini`，UTF-8 编码，`key=value` 格式：

```ini
# dsh-manager configuration
repo=C:\Users\me\source\repos\deepseek-harness
port=3080
logdir=
```

- `repo` — 仓库路径。未设置或无效时自动探测：从可执行文件所在目录逐级向上查找名为 `@deepseek-ai/dsh-root` 的 `package.json`，再尝试常见位置（`%USERPROFILE%\source\repos\deepseek-harness`、`%USERPROFILE%\deepseek-harness`、`%USERPROFILE%\code\deepseek-harness`）。路径框旁的 `…` 按钮可手动选择目录。
- `port` — 管理器轮询与打开浏览器所用的端口。必须与 `dsh web` 配置实际绑定的端口一致；管理器不修改服务端端口。
- `logdir` — `dsh-web-out.log` / `dsh-web-err.log` 的写入目录。留空表示 `%LOCALAPPDATA%\dsh-manager\logs`。

## 构建

在本目录运行 `build.cmd`。它定位系统自带的 `csc.exe`，在源码旁生成 `DSHManager.exe`（已 gitignore）。可执行文件可复制到任意位置；配置文件与它同目录。

## 自检

`DSHManager.exe --selftest` 运行非交互检查——配置读写回环、工具可用性（`git`、`node`、`corepack`、`csc`）、进程匹配规则、仓库自动探测、日志目录可写性、服务可达性（仅提示）——并把报告写入日志目录。退出码 `0` 表示全部通过。

## 已知限制

- 进程匹配是启发式的：命令行包含 `pnpm dsh web`，或同时包含 `src/bin.ts` 与 `web` 参数的 node 进程会被当作 dsh web 服务。以 `src/bin.ts web` 方式启动的其他 tsx 项目也会被误匹配。
- 关闭服务会终止其所承载的 agent 会话；管理器仅在「更新并启动」和「关闭服务」时主动执行。
- 更新流程假定本仓库的目录结构（`build:lib` 与 `@deepseek-ai/dsh-web-frontend`）。
