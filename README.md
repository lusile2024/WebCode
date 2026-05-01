# WebCode

<p align="center">
  <a href="README.md">简体中文</a> | <a href="README_EN.md">English</a>
</p>

<p align="center">
  <strong>把 AI CLI、Web 会话、移动端和飞书工作流接到同一个控制面板里</strong>
</p>

<p align="center">
  通过浏览器或飞书卡片管理 Claude Code、Codex、OpenCode 会话、工作区、项目和命令执行。
</p>

---

## 项目简介

WebCode 是一个基于 `Blazor Server + .NET 10` 的 AI CLI 工作平台。它不是单机聊天壳，而是把本地或服务器上的 AI CLI 包装成一个可管理、可部署、可协作、可远程访问的工作系统。

当前仓库已经覆盖这些核心场景：

- 在 Web 端创建、切换、关闭和恢复 AI 会话
- 为每个会话绑定独立工作区和项目目录
- 导入当前系统账户下已有的 `Claude Code`、`Codex`、`OpenCode` 本地会话
- 从原始 CLI transcript 恢复历史消息，而不只依赖 WebCode 自己的消息存档
- 在桌面端、移动端和飞书卡片里使用同一批会话
- 为团队用户配置可用工具、目录白名单、飞书机器人和权限
- 把 `cc-switch` 作为受管 CLI 的唯一 Provider 权威源

如果你需要的是“可部署的 AI CLI 控制台”，而不是单机本地包装器，这个仓库就是为这个目标设计的。

## 当前重点能力

### 多 AI CLI 接入

当前已适配或直接支持：

| 工具 | 说明 | 状态 |
|------|------|------|
| `Claude Code` | 会话管理、流式输出、Transcript 恢复 | 可用 |
| `Codex CLI` | JSONL 输出、沙箱/审批模式、会话执行 | 可用 |
| `OpenCode` | 多模型工作流、会话导入与执行 | 可用 |
| 自定义 CLI | 可继续按适配器模式扩展 | 可扩展 |

相关实现主要位于 [WebCodeCli.Domain/Domain/Service/Adapters](./WebCodeCli.Domain/Domain/Service/Adapters)。

### Web、移动端、飞书统一会话流

- 每个会话可绑定独立工作区
- 支持默认目录、已有目录、项目目录和自定义目录
- 支持文件浏览、预览、上传、复制路径等工作区操作
- 支持历史会话切换、关闭、隔离和清理
- 支持导入本地 CLI 会话并继续在 WebCode 中使用
- 支持在移动端抽屉和飞书会话卡片里直接管理会话

### 多用户与权限控制

- 用户启用/禁用
- 用户可用 CLI 工具限制
- 用户白名单目录策略
- 用户级飞书机器人配置
- 默认共享配置 + 用户覆盖配置

### 外部会话导入与恢复

外部 CLI 会话导入当前具备这些约束：

- 只扫描当前操作系统账户下可访问的本地会话
- 只显示工作区落在允许目录或白名单中的会话
- 同一个 `(ToolId, CliThreadId)` 只能被一个 WebCode 用户占用
- Web 端与飞书端都支持分页浏览、导入并切换到这些会话

### Windows 安装包发布

仓库已经内置 Windows 安装包构建脚本，可直接生成：

- 安装版 `WebCode-Setup-vX.Y.Z-win-x64.exe`
- 便携版 `WebCode-vX.Y.Z-win-x64-portable.zip`
- 校验文件 `SHA256SUMS.txt`
- Release 说明 `RELEASE_NOTES.md`

## `cc-switch` 托管模型

从当前版本开始，`Claude Code`、`Codex`、`OpenCode` 这三类受管助手遵循同一套规则：

1. `cc-switch` 是唯一 Provider 权威源。
2. WebCode 不再允许为这三类工具手动填写环境变量、编辑 Provider 或切换 Profile。
3. Provider 的激活与切换必须在 `cc-switch` 中完成。
4. WebCode 只读取 `cc-switch` 的当前状态和 live config。

WebCode 会读取这些来源：

- `~/.cc-switch/settings.json`
- `~/.cc-switch/cc-switch.db`
- `cc-switch` 写出的各工具 live 配置文件

### 会话级 Provider 快照

WebCode 现在遵循“像终端窗口一样”的会话语义：

- 新会话在第一次运行时，会复制当前激活 Provider 的 live config 作为该会话的快照
- 已经在使用中的旧会话，不会因为你在 `cc-switch` 里切换了 Provider 而自动跟着变
- 只有当你显式点击“同步 Provider”时，旧会话才会切换到 `cc-switch` 当前激活的 Provider

这个同步入口当前已经补齐到：

- 桌面 Web 会话列表
- 移动端会话抽屉
- 飞书会话管理卡片

如果会话快照丢失或损坏，WebCode 会阻止继续执行，并提示你显式同步，而不是悄悄回退到机器当前 live 配置。

### OpenCode 的启用方式

`OpenCode` 和 `Claude Code`、`Codex` 一样受 `cc-switch` 管理。

- 初始化时可以选择启用哪些受管助手
- 如果初始化时没勾选，后续也可以在设置里的助手管理中重新启用
- 是否真正可用，取决于 `cc-switch` 当前是否已经为该工具准备好可启动的 Provider 和 live config

## 快速开始

### 方式一：Docker 部署

适合试用、内网部署和小团队使用。

```bash
git clone https://github.com/lusile2024/WebCode.git
cd WebCode
docker compose up -d
```

启动后访问：

- `http://localhost:5000`

更多部署细节可查看：

- [QUICKSTART.md](./QUICKSTART.md)
- [DEPLOY_DOCKER.md](./DEPLOY_DOCKER.md)
- [docs/Docker-CLI-集成部署指南.md](./docs/Docker-CLI-集成部署指南.md)

### 方式二：Windows Release 包

适合直接在 Windows 机器上运行 WebCode，不需要预装 `.NET SDK`。

下载入口：

- [GitHub Releases](https://github.com/lusile2024/WebCode/releases/latest)

当前 Windows 发布资产包括：

- `WebCode-Setup-vX.Y.Z-win-x64.exe`
- `WebCode-vX.Y.Z-win-x64-portable.zip`
- `SHA256SUMS.txt`
- `RELEASE_NOTES.md`

安装版与便携版都使用 `win-x64` 自包含运行时。默认首次启动后访问：

- `http://localhost:6021`

程序目录下默认会准备：

- `data/`
- `logs/`
- `workspaces/`

安装版升级时会保留已有的 `appsettings.json`。

### 方式三：本地开发运行

适合调试、二次开发和本地联调。

#### 环境要求

- `.NET 10 SDK`
- `cc-switch` 已正确安装并已为目标受管工具完成配置
- 已安装 `claude`、`codex`、`opencode` 等目标 CLI

#### 启动命令

```bash
git clone https://github.com/lusile2024/WebCode.git
cd WebCode
dotnet restore
dotnet run --project WebCodeCli
```

默认访问地址：

- `http://localhost:6021`

如果你修改了根目录 [appsettings.json](./appsettings.json) 或 [WebCodeCli/appsettings.json](./WebCodeCli/appsettings.json) 中的 `urls` 配置，请以实际监听端口为准。

## 首次初始化建议

当前初始化流程建议按这个顺序完成：

1. 创建管理员账户
2. 确认是否开启登录认证
3. 选择希望在 WebCode 中启用的受管助手
4. 在向导里检查 `cc-switch` 就绪状态，并确保所选助手都已经可启动
5. 验证工作区根目录和存储目录
6. 如需飞书集成，再配置飞书机器人参数
7. 如需多用户，进入管理界面配置用户、目录白名单和工具权限
8. 如需恢复已有上下文，可导入本地外部 CLI 会话

初始化向导不再承担这些职责：

- 手动录入受管工具环境变量
- 手动切换受管工具 Provider
- 手动编辑受管工具 live config

这些动作现在都必须在 `cc-switch` 中完成。

## 配置说明

### 受管助手配置

对 `Claude Code`、`Codex`、`OpenCode` 来说：

- Provider 切换只能在 `cc-switch` 里做
- WebCode 设置页只能查看 `cc-switch` 状态，不能手动编辑环境变量
- 助手管理页可以启用或停用助手，但不会替代 `cc-switch` 管理 Provider
- 会话级“同步 Provider”只会把当前 `cc-switch` 激活状态复制到指定会话，不会改写 `cc-switch`

### 自定义 CLI 配置

如果你要接入非 `cc-switch` 受管的自定义 CLI，仍然可以通过 `CliTools.Tools` 扩展工具定义。相关文档可参考：

- [cli/README.md](./cli/README.md)
- [docs/CLI工具配置说明.md](./docs/CLI工具配置说明.md)
- [docs/Codex配置说明.md](./docs/Codex配置说明.md)

### Docker 数据目录

`docker-compose.yml` 默认会挂载这些目录：

- `./webcodecli-data`：数据库与运行数据
- `./webcodecli-workspaces`：工作区目录
- `./webcodecli-logs`：日志

### 数据库

默认数据库为 SQLite：

- `WebCodeCli.db`

本地开发默认连接字符串来自根目录 [appsettings.json](./appsettings.json)。

## Windows 发布维护

仓库内置安装包构建脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-windows-installer.ps1
```

脚本会读取 [Directory.Build.props](./Directory.Build.props) 中的版本号，并在 `artifacts/windows-installer/vX.Y.Z/` 下生成：

- `publish/`
- `WebCode-vX.Y.Z-win-x64-portable.zip`
- `installer/WebCode-Setup-vX.Y.Z-win-x64.exe`
- `SHA256SUMS.txt`
- `RELEASE_NOTES.md`

构建机要求：

- Windows
- `.NET 10 SDK`
- `Inno Setup 6`

## 项目结构

```text
WebCode/
├── WebCodeCli/                # Web 应用（Blazor Server）
├── WebCodeCli.Domain/         # 领域服务、仓储、CLI/飞书适配
├── WebCodeCli.Domain.Tests/   # 领域层测试
├── tests/WebCodeCli.Tests/    # Web / 集成相关测试
├── cli/                       # CLI 使用说明
├── docs/                      # 额外文档
├── docker-compose.yml         # Docker 部署入口
└── README.md
```

当前实际项目名为：

- `WebCodeCli`
- `WebCodeCli.Domain`

如果你在旧文档或历史笔记里看到 `WebCode` / `WebCode.Domain` 这样的旧路径描述，请以仓库真实目录为准。

## 技术栈

| 类别 | 技术 |
|------|------|
| Web 框架 | Blazor Server |
| 运行时 | .NET 10 |
| 编辑器 | Monaco Editor |
| 数据访问 | SqlSugar |
| 默认数据库 | SQLite |
| 反向代理 | YARP |
| Markdown | Markdig |
| AI CLI 集成 | Claude Code / Codex / OpenCode |

## 常用文档

- [QUICKSTART.md](./QUICKSTART.md)
- [DEPLOY_DOCKER.md](./DEPLOY_DOCKER.md)
- [docs/QUICKSTART_CodeAssistant.md](./docs/QUICKSTART_CodeAssistant.md)
- [docs/README_CodeAssistant.md](./docs/README_CodeAssistant.md)
- [docs/workspace-management-guide.md](./docs/workspace-management-guide.md)
- [docs/workspace-management-deployment-guide.md](./docs/workspace-management-deployment-guide.md)

## 许可证

本项目采用 [AGPLv3](LICENSE)。

- 开源使用：遵循 AGPLv3
- 商业授权：请联系仓库维护者或相关商务渠道

---

<p align="center">
  <strong>让 AI CLI 从“本地命令”升级为“可管理、可部署、可协作的工作平台”</strong>
</p>
