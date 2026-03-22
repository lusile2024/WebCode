namespace WebCodeCli.Domain.Domain.Service;

public enum UserFeishuBotRuntimeState
{
    NotConfigured = 0,
    Stopped = 1,
    Starting = 2,
    Connected = 3,
    Failed = 4,
    Stopping = 5
}
