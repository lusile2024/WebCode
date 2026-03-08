
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

WebCode 是一个在线 AI 全能工作平台，通过 Web 浏览器远程控制各种 AI CLI 助手，支持编程、文档处理、需求分析、报告撰写等全方位工作场景。

- **技术栈**: .NET 10.0 Blazor Server + Tailwind CSS + SqlSugar ORM
- **AI 工具**: Claude Code CLI、Codex CLI、OpenCode CLI
- **架构模式**: 领域驱动设计 (DDD) + 模块化服务注册

## 项目结构

```
WebCode/
├── WebCodeCli/              # 主应用程序（Blazor Server）
│   ├── Pages/               # Blazor 页面
│   │   ├── CodeAssistant.razor/.cs    # 桌面端主界面
│   │   ├── CodeAssistantMobile.razor/.cs # 移动端界面
│   │   └── Setup.razor      # 初始化设置向导
│   ├── Components/          # 可重用 Blazor 组件
│   ├── Controllers/         # API 控制器
│   ├── wwwroot/             # 静态资源
│   └── Program.cs           # 应用入口点
│
├── WebCodeCli.Domain/       # 领域层（DDD）
│   ├── Common/
│   │   ├── Extensions/      # 服务注册扩展
│   │   ├── Options/         # 配置选项类
│   │   └── Map/             # AutoMapper 配置
│   ├── Domain/
│   │   ├── Model/           # 领域模型
│   │   ├── Service/         # 领域服务
│   │   │   ├── Adapters/    # CLI 工具适配器
│   │   │   └── Channels/    # 飞书渠道服务
│   │   └── Exceptions/      # 自定义异常
│   └── Repositories/        # 数据仓储（SqlSugar）
│
├── docs/                     # 技术文档
├── cli/                      # CLI 工具文档
└── docker/                   # Docker 部署配置
```

## 常用命令

### 构建和运行

```bash
# 恢复 NuGet 包
dotnet restore

# 构建解决方案
dotnet build WebCodeCli.sln

# 运行应用（开发模式）
dotnet run --project WebCodeCli

# 发布应用
dotnet publish WebCodeCli.sln -c Release -o publish
```

### Docker 部署

```bash
# 一键启动
docker compose up -d

# 构建镜像
docker build -t webcodecli:latest .

# 查看日志
docker logs webcodecli
```

### Tailwind CSS 构建

```bash
cd WebCodeCli
npm install
npm run build:css
```

## 核心架构模式

### 服务注册模式

使用自定义 `[ServiceDescription]` 属性自动注册服务：

```csharp
[ServiceDescription(Lifetime = ServiceLifetime.Singleton)]
public class MyService : IMyService { ... }

// 在 Program.cs 中自动发现
builder.Services.AddServicesFromAssemblies("WebCodeCli", "WebCodeCli.Domain");
```

### CLI 适配器模式

在 `WebCodeCli.Domain/Domain/Service/Adapters/` 目录下添加新的 AI CLI 工具适配器。

### 后台服务模式

飞书渠道服务等继承自 `BackgroundService`，在应用启动时自动运行。

## 关键文件

| 文件路径 | 说明 |
|---------|------|
| `WebCodeCli/Program.cs` | 应用入口点，服务配置 |
| `WebCodeCli/appsettings.json` | 应用配置文件 |
| `WebCodeCli/Pages/CodeAssistant.razor.cs` | 桌面端主界面逻辑（2000+ 行）|
| `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs` | CLI 工具执行服务（核心引擎）|
| `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs` | 飞书渠道服务 |

## 开发约定

- 使用中文注释
- 服务通过 `[ServiceDescription]` 属性自动注册
- 配置通过 `IOptions&lt;T&gt;` 模式访问
- 每个会话有独立的临时工作目录

---

## 飞书卡片开发参考文档

### 核心规范
| 文档名称 | 链接 |
|---------|------|
| 卡片JSON 2.0版本更新说明 | [卡片JSON-v2变更说明.md](./docs/feishu/cards/卡片JSON-v2变更说明.md) |
| 卡片JSON 2.0结构 | https://open.feishu.cn/document/feishu-cards/card-json-v2-structure |
| 卡片JSON 2.0版本组件概述 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/component-json-v2-overview |

### 容器组件
| 组件名称 | 链接 |
|---------|------|
| 分栏 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/containers/column-set |
| 表单容器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/containers/form-container |
| 交互容器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/containers/interactive-container |
| 折叠面板 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/containers/collapsible-panel |

### 展示组件
| 组件名称 | 链接 |
|---------|------|
| 标题 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/title |
| 普通文本 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/plain-text |
| 富文本（Markdown） | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/rich-text |
| 图片 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/image |
| 多图混排 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/multi-image-laylout |
| 人员 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/user-profile |
| 人员列表 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/user-list |
| 图表 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/chart |
| 表格 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/table |
| 音频 | https://open.feishu.cn/document/uAjLw4CM/ukzMukzMukzM/feishu-cards/card-json-v2-components/content-components/audio |
| 分割线 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/content-components/divider |

### 交互组件
| 组件名称 | 链接 |
|---------|------|
| 输入框 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/input |
| 按钮 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/button |
| 折叠按钮组 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/overflow |
| 下拉选择-单选 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/single-select-dropdown-menu |
| 下拉选择-多选 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/multi-select-dropdown-menu |
| 人员选择-单选 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/single-select-user-picker |
| 人员选择-多选 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/multi-select-user-picker |
| 日期选择器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/date-picker |
| 时间选择器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/time-selector |
| 日期时间选择器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/date-time-picker |
| 多图选择 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/image-picker |
| 勾选器 | https://open.feishu.cn/document/feishu-cards/card-json-v2-components/interactive-components/checker |
