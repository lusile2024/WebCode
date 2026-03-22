namespace WebCodeCli.Domain.Domain.Service;

public sealed class UserFeishuBotRuntimeStatus
{
    public string Username { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public UserFeishuBotRuntimeState State { get; set; } = UserFeishuBotRuntimeState.NotConfigured;
    public bool IsConfigured { get; set; }
    public bool CanStart { get; set; }
    public string? Message { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
