# Web 端会话管理增强设计文档

## 1. 功能概述

为 Web 端实现用户-会话绑定和自定义工作目录功能，与飞书端保持一致的体验：
- 用户只能查询和管理自己的会话
- 创建会话时可以选择自定义工作目录（需白名单校验）
- 支持桌面端和移动端友好的操作界面
- 安全校验复用现有成熟逻辑

## 2. 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
├─────────────────────┬───────────────────────────────────────┤
│   CodeAssistant     │        FeishuChannelService            │
│   (Web 端)          │        (飞书端)                        │
└─────────┬───────────┴───────────────┬───────────────────────┘
          │                           │
          │         IWorkspaceService │
          └───────────────┬───────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                   IWorkspaceService                          │
│  - ValidatePath(path) → (isValid, errorMessage)             │
│  - EnsurePathExists(path, allowCreate) → (success, error)   │
│  - GetAllowedPaths() → List<string>                         │
│  - GetOrCreateWorkspace(sessionId, customPath?) → path      │
└─────────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                  PathSecurityHelper                          │
│  (现有实现，复用)                                            │
└─────────────────────────────────────────────────────────────┘
```

## 3. 数据模型变更

```csharp
public class SessionHistory
{
    // 现有字段
    public string SessionId { get; set; }
    public string Title { get; set; }
    public string Username { get; set; }        // 用户标识
    public string? WorkspacePath { get; set; }  // 工作目录路径

    // 新增字段
    public bool IsCustomWorkspace { get; set; } // 是否为自定义目录
}
```

## 4. 核心流程

### 创建会话流程
1. 用户点击"新建会话"
2. 选择"使用默认目录"或"自定义目录"
3. 如果自定义目录：
   - 调用 `IWorkspaceService.ValidatePath()` 校验路径
   - 校验通过则继续，否则提示错误
4. 调用 `IWorkspaceService.GetOrCreateWorkspace()` 创建工作目录
5. 保存会话信息到数据库，绑定当前用户

### 会话查询流程
1. 获取当前登录用户名
2. 数据库查询该用户的所有会话
3. 按更新时间倒序返回给前端

## 5. 安全设计

1. **路径校验**：所有自定义路径都经过 `PathSecurityHelper` 校验
   - 禁止根目录
   - 禁止系统敏感目录
   - 必须在白名单目录范围内

2. **用户隔离**：
   - 所有会话查询都绑定当前用户
   - 会话操作（切换/删除/修改）都校验用户权限

3. **目录清理策略**：
   - 默认目录会话：删除时自动清理工作目录
   - 自定义目录会话：删除时保留工作目录（支持多会话共用）

## 6. UI 设计

- **桌面端**：侧边栏快速切换 + 独立会话管理面板
- **移动端**：顶部下拉菜单，支持触摸友好的交互
- **新建会话弹窗**：支持默认目录/自定义目录选择
- **目录白名单**：通过 `/webdir` 命令查看允许的目录列表

## 7. API 和命令设计

### 新增 Web API
| API 路径 | 方法 | 说明 |
|---------|------|------|
| `/api/workspace/allowed-paths` | GET | 获取目录白名单 |
| `/api/workspace/validate-path` | POST | 验证路径是否合规 |
| `/api/sessions/user` | GET | 获取当前用户的所有会话 |

### 新增内置命令
| 命令 | 说明 |
|------|------|
| `/webdir` | 查看当前用户允许的目录白名单（类似 `/feishudir`） |
| `/websessions` | 打开会话管理面板 |