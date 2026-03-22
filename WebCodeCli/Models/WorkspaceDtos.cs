using System.Text.Json.Serialization;

namespace WebCodeCli.Models;

/// <summary>
/// 可访问目录DTO
/// </summary>
public class AccessibleDirectoryDto
{
    public int Id { get; set; }
    [JsonPropertyName("DirectoryPath")]
    public string Path { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public bool IsTrusted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Permission { get; set; } = "read";
    public string? GrantedBy { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Type { get; set; } = "owned";
}

/// <summary>
/// API响应基类
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 创建会话选项
/// </summary>
public class CreateSessionOptions
{
    public bool UseDefaultDirectory { get; set; } = true;
    public string WorkspacePath { get; set; } = string.Empty;
}

/// <summary>
/// 更新会话工作区请求
/// </summary>
public class UpdateSessionWorkspaceRequest
{
    public string DirectoryPath { get; set; } = string.Empty;
}

/// <summary>
/// 工作区授权DTO
/// </summary>
public class WorkspaceAuthorizationDto
{
    public int Id { get; set; }
    public string AuthorizedUsername { get; set; } = string.Empty;
    public string Permission { get; set; } = "read";
    public string GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// 目录授权请求
/// </summary>
public class AuthorizeDirectoryRequest
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string AuthorizedUsername { get; set; } = string.Empty;
    public string Permission { get; set; } = "read";
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// 取消授权请求
/// </summary>
public class RevokeAuthorizationRequest
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string AuthorizedUsername { get; set; } = string.Empty;
}
