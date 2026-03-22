# WebCode

<p align="center">
  <a href="README.md">简体中文</a> | <a href="README_EN.md">English</a>
</p>

<p align="center">
  <strong>A workspace platform that connects AI CLIs, web sessions, multi-user controls, and Feishu bots</strong>
</p>

<p align="center">
  Manage AI sessions, workspaces, projects, and command execution from the browser or Feishu cards, with desktop and mobile support.
</p>

---

## Overview

WebCode is an AI workspace platform built on `Blazor Server + .NET 10`. It is not just a chat UI. The goal is to turn local or server-side AI CLI tools into a manageable, collaborative, remotely accessible working system.

Typical scenarios already covered by the project include:

- Creating and managing AI sessions from the web UI
- Binding a dedicated workspace to each session
- Managing sessions, projects, and directories through Feishu bots and cards
- Configuring per-user CLI environments, Feishu bots, workspace allowlists, and tool permissions
- Using the same workflow across desktop, tablet, and mobile browsers

If you need a deployable control plane for AI CLIs instead of a single-user local wrapper, this repository is designed for that use case.

## Core Capabilities

### 1. Multiple AI CLI integrations

The repository already includes adapters or runtime support for the following tools:

| Tool | Notes | Status |
|------|-------|--------|
| `Claude Code` | Session management, streaming output, adapter parsing | Available |
| `Codex CLI` | JSONL output, sandbox/approval modes, session execution | Available |
| `OpenCode` | Multi-model workflow integration | Available |
| Other CLIs | Can be added with the same adapter pattern | Extensible |

Related implementations are primarily located in [WebCodeCli.Domain/Domain/Service/Adapters](./WebCodeCli.Domain/Domain/Service/Adapters).

### 2. Web session and workspace management

- Each session can use its own workspace
- Supports default directories, existing folders, project folders, and custom paths
- Supports file browsing, preview, upload, and path copy operations
- Supports session history, switching, closing, isolation, and cleanup
- Can create sessions directly from project management flows

### 3. Multi-user controls and permissions

The current system already includes the foundations for multi-user operation:

- Enable or disable users
- Restrict which CLI tools each user can use
- Configure per-user workspace allowlists
- Configure per-user CLI environment variables
- Configure per-user Feishu bot settings
- Use shared defaults with user-level overrides

That means the project is no longer just a local single-user tool. It can already operate as an internal team platform.

### 4. Feishu bots and card-driven workflows

The Feishu integration is more than simple message forwarding. It acts as a full operational entry point:

- Users can bind their own Feishu bots
- Session management cards
- Project management cards
- Workspace allowlist browsing
- Project clone, pull, and branch switching
- Plain text completion notifications after session work finishes

Relevant code is mainly located in [WebCodeCli.Domain/Domain/Service/Channels](./WebCodeCli.Domain/Domain/Service/Channels).

### 5. Unified desktop and mobile experience

- Responsive layouts
- Mobile-friendly navigation and input areas
- Works on phones and tablets
- Shares the same session, file, preview, and settings workflow across devices

Mobile UI preview:

![Mobile UI](images/mobile.png)

## Screenshots

> The images below are bundled repo assets used to illustrate the product and workflow.

![Coding assistant](images/coding.png)
![PPT and document helper](images/ppt.png)
![Skills and workflows](images/skill.png)
![Games and creative examples](images/games.png)

## Quick Start

### Option 1: Docker deployment

This is the most direct way to start the system and is suitable for trials, internal deployment, and small team usage.

```bash
git clone https://github.com/lusile2024/WebCode.git
cd WebCode
docker compose up -d
```

After startup, open:

- `http://localhost:5000`

The first visit will guide you through initialization.

For more deployment details, see:

- [QUICKSTART.md](./QUICKSTART.md)
- [DEPLOY_DOCKER.md](./DEPLOY_DOCKER.md)
- [docs/Docker-CLI-集成部署指南.md](./docs/Docker-CLI-集成部署指南.md)

### Option 2: Local development

This is the better option for debugging, customization, and local integration work.

#### Requirements

- `.NET 10 SDK`
- Installed target AI CLIs such as `claude`, `codex`, or `opencode`

#### Start commands

```bash
git clone https://github.com/lusile2024/WebCode.git
cd WebCode
dotnet restore
dotnet run --project WebCodeCli
```

Default local URL:

- `http://localhost:5000`

## Recommended first-time setup

For the initial setup, the recommended order is:

1. Create the administrator account.
2. Decide whether login authentication should be enabled.
3. Configure environment variables for at least one usable CLI tool.
4. Verify the workspace root and storage directories.
5. Configure Feishu bot settings if Feishu integration is needed.
6. Open the management UI to configure users, workspace allowlists, and tool permissions if multi-user mode is needed.

Setup wizard preview:

![Setup wizard - Step 1](images/setup1.png)
![Setup wizard - Step 2](images/setup2.png)
![Setup wizard - Step 3](images/setup3.png)

## Configuration

### CLI configuration

CLI tool configuration can be maintained through either the UI or configuration files.

A typical configuration shape looks like this:

```json
{
  "CliTools": {
    "Tools": [
      {
        "Id": "claude-code",
        "Name": "Claude Code",
        "Command": "claude",
        "ArgumentTemplate": "-p \"{prompt}\"",
        "Enabled": true
      },
      {
        "Id": "codex",
        "Name": "Codex",
        "Command": "codex",
        "ArgumentTemplate": "exec \"{prompt}\"",
        "Enabled": true
      }
    ]
  }
}
```

More detailed references:

- [cli/README.md](./cli/README.md)
- [docs/CLI工具配置说明.md](./docs/CLI工具配置说明.md)
- [docs/Codex配置说明.md](./docs/Codex配置说明.md)

### Docker data directories

By default, `docker-compose.yml` mounts the following directories:

- `./webcodecli-data` for database and runtime data
- `./webcodecli-workspaces` for workspace storage
- `./webcodecli-logs` for logs

### Database

The default database is SQLite:

- `WebCodeCli.db`

For local development, the default connection string comes from [appsettings.json](./appsettings.json).

## Multi-user and Feishu deployment notes

If you plan to run WebCode as a team service, these points matter first:

- Define separate workspace allowlists for each user
- Restrict which CLI tools each user may use
- Bind a dedicated Feishu bot per user
- Keep database files out of Git
- Separate shared default settings from user-specific overrides

Code related to multi-user and Feishu capabilities is mainly distributed across:

- [WebCodeCli/Controllers](./WebCodeCli/Controllers)
- [WebCodeCli/Components](./WebCodeCli/Components)
- [WebCodeCli.Domain/Domain/Service/Channels](./WebCodeCli.Domain/Domain/Service/Channels)
- [WebCodeCli.Domain/Repositories/Base/UserFeishuBotConfig](./WebCodeCli.Domain/Repositories/Base/UserFeishuBotConfig)

## Project structure

The main repository structure is:

```text
WebCode/
├── WebCodeCli/                # Web application (Blazor Server)
├── WebCodeCli.Domain/         # Domain services, repositories, CLI and Feishu adapters
├── WebCodeCli.Domain.Tests/   # Domain-layer tests
├── tests/WebCodeCli.Tests/    # Web and integration-related tests
├── cli/                       # CLI usage documentation
├── docs/                      # Additional documentation
├── docker-compose.yml         # Docker deployment entrypoint
└── README.md
```

Compared with older documentation, the actual project names are now:

- `WebCodeCli`
- `WebCodeCli.Domain`

If you still see old references such as `WebCode` or `WebCode.Domain` in historical docs or notes, use the real repository structure instead.

## Tech stack

| Category | Technology |
|----------|------------|
| Web framework | Blazor Server |
| Runtime | .NET 10 |
| Editor | Monaco Editor |
| Data access | SqlSugar |
| Default database | SQLite |
| Reverse proxy | YARP |
| Markdown | Markdig |
| AI CLI integration | Claude Code, Codex, OpenCode, and more |

## Useful documentation

- [QUICKSTART.md](./QUICKSTART.md)
- [DEPLOY_DOCKER.md](./DEPLOY_DOCKER.md)
- [docs/QUICKSTART_CodeAssistant.md](./docs/QUICKSTART_CodeAssistant.md)
- [docs/README_CodeAssistant.md](./docs/README_CodeAssistant.md)
- [docs/workspace-management-guide.md](./docs/workspace-management-guide.md)
- [docs/workspace-management-deployment-guide.md](./docs/workspace-management-deployment-guide.md)

## Public demo and community

If you keep the current public demo online, the following information can still be used:

| URL | Username | Password |
|-----|----------|----------|
| [https://webcode.tree456.com/](https://webcode.tree456.com/) | `treechat` | `treechat@123` |

> Public demo environments are for evaluation only and should not be used for sensitive data.

Community QR code:

<p align="center">
  <img src="images/qrcode.jpg" alt="WeChat group QR code" width="200" />
</p>

## License

This project is licensed under [AGPLv3](LICENSE).

- Open source use: follow AGPLv3
- Commercial licensing: contact the repository maintainer or relevant business channel

---

<p align="center">
  <strong>Turn AI CLIs from local commands into a manageable working platform</strong>
</p>
