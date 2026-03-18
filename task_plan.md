# Task Plan: 飞书项目管理与 TFS 仓库支持

## Goal
为飞书渠道补齐接近 Web 端的项目管理能力，重点支持 Git 项目的创建、查看、克隆、拉取和基于项目创建会话；同时让公司内部 TFS Git 仓库 `http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4` 能通过“账号 + 密码/Token”方式完成克隆。

## Scope
- 飞书端新增项目管理入口和卡片交互。
- 复用现有项目库、项目服务、Git 服务和 Web 用户绑定。
- 扩展现有 HTTP/HTTPS Git 认证链路，使其可用于内部 TFS 仓库。
- 修正当前项目创建默认强制 `main` 分支的问题，改为允许空分支并使用远端默认分支。

## Explicit Decisions
- TFS 认证优先支持“账号 + 密码/Token”。
- 不新增单独的 TFS 专属项目类型，继续复用现有 Git 项目模型。
- 现有 `https` 认证语义扩展为“HTTP/HTTPS 用户名 + Secret（密码或 Token）”，尽量避免数据库字段迁移。
- 飞书第一阶段只做 Git 项目管理；ZIP 上传项目仍保留在 Web 端，不在本轮飞书范围内。
- 飞书项目管理入口采用双入口：
  - 新增 `/feishuprojects`
  - 在现有飞书会话管理卡片中增加“项目管理”按钮

## Current Findings
- Web 端已有完整项目管理后端：
  - `ProjectController` 提供项目 CRUD、克隆、拉取、分支获取接口。
  - `ProjectService` 负责项目配置保存、克隆、拉取、目录注册。
  - `GitService` 已支持公开仓库、HTTPS Token、SSH。
- 飞书端已经有：
  - Web 用户绑定能力，可解析出绑定的 Web 用户名。
  - 卡片动作回调框架、会话管理卡片、新建会话卡片。
  - 作用域内服务解析能力，可在飞书动作里拿到 scoped service。
- 当前后端存在两个与 TFS 直接相关的问题：
  - 项目默认分支被强制写成 `main`，对 TFS/旧仓库不安全。
  - Git 的 HTTP 认证实现更偏向公开 HTTPS/Token，不足以稳妥覆盖内部 TFS 的账号密码场景。

## Implementation Plan

### Phase 1: Git/TFS 克隆基础能力
- 调整 `ProjectInfo` / `CreateProjectRequest` / `UpdateProjectRequest` / `GetBranchesRequest` 的文案语义：
  - `HttpsUsername` 继续保留字段名，但 UI/飞书表单改为“HTTP/HTTPS 用户名”
  - `HttpsToken` 改为“密码或 Token”
- 修改 `ProjectService.CreateProjectAsync`：
  - 不再默认把空分支写成 `main`
  - 允许 `Branch` 为空字符串
- 修改 `GitService`：
  - 为 HTTP/HTTPS 认证增加 CLI 路径，使用显式 Basic 认证头或等价方式执行：
    - `clone`
    - `pull`
    - `ls-remote --heads`
  - 兼容 `http://` 的 TFS 地址，不再假设必须是 `https://`
  - 保留现有 SSH 路径
- 目标结果：
  - 绑定用户名后，既可用密码，也可用 Token 克隆 TFS 仓库
  - 分支留空时按远端默认分支克隆

### Phase 2: 飞书侧项目服务接入
- 在飞书卡片动作处理中增加一个“按绑定 Web 用户执行项目操作”的统一入口：
  - 新建 scope
  - 解析绑定的 Web 用户名
  - 对同一 scope 中的 `IUserContextService` 调用 `SetCurrentUsername`
  - 再调用 `IProjectService`
- 这样可以不大改 `IProjectService` 公开接口，直接复用现有项目能力。

### Phase 3: 飞书项目管理卡片
- 新增命令入口：
  - `/feishuprojects`
- 新增卡片动作：
  - `open_project_manager`
  - `show_create_project_form`
  - `create_project`
  - `clone_project`
  - `pull_project`
  - `delete_project`
  - `show_edit_project_form`
  - `update_project`
  - `fetch_project_branches`
  - `create_session_from_project`
- 项目管理卡片展示：
  - 项目名称
  - 仓库地址
  - 分支
  - 状态：pending / cloning / ready / error
  - 最后同步时间
  - 错误信息
- 支持的飞书交互能力：
  - 新建 Git 项目
  - 编辑 Git 项目
  - 克隆项目
  - 拉取项目
  - 删除项目
  - 从 ready 项目直接创建新会话
- 第一阶段不做：
  - ZIP 上传项目
  - 飞书端复杂批量操作
  - 项目列表的无限滚动

### Phase 4: Web 端兼容补齐
- 更新 Web 项目管理表单文案与提示：
  - 明确“HTTP/HTTPS 用户名”
  - 明确“密码或 Token”
  - 增加 TFS/内网 Git 兼容提示
- 保持 Web 原有功能不回退。

## Verification Plan
- 单元测试/服务测试：
  - 新增飞书项目管理动作测试：
    - 打开项目管理卡片
    - 创建项目
    - 克隆项目
    - 拉取项目
    - 从项目创建会话
  - 新增 Git/TFS 认证相关测试：
    - HTTP 认证命令构造
    - 空分支时不强制传 `main`
    - TFS `http://` URL 的认证路径选择
- 回归测试：
  - 现有 `Feishu*` 测试继续通过
  - 现有项目管理相关测试/编译继续通过
- 命令验证：
  - `dotnet test WebCodeCli.Domain.Tests --filter Feishu`
  - `dotnet test WebCodeCli.Domain.Tests --filter Project`
  - 如需要，再补充 `GitService` 定向测试

## Risks And Mitigations
- 风险：TFS 服务器若只接受 Windows 集成认证而不接受 Basic/Token，账号密码仍可能失败。
  - 处理：本轮先实现显式账号密码/Token；若目标环境仍要求系统凭据，再追加“依赖系统凭据”回退路径。
- 风险：飞书卡片表单字段多，交互复杂。
  - 处理：第一阶段只覆盖 Git 项目核心字段，不把 ZIP 上传搬过去。
- 风险：长时间克隆任务在飞书回调里体验不好。
  - 处理：先返回 toast，再异步执行并刷新项目状态。

## Success Criteria
- 飞书里可通过卡片创建一个指向 TFS 仓库的 Git 项目。
- 使用账号 + 密码/Token 可以成功克隆该项目。
- 克隆成功后项目目录自动注册，可直接用于飞书新会话。
- 飞书和 Web 看到的是同一套项目数据。
