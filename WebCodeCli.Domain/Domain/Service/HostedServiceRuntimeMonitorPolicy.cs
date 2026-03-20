using Microsoft.Extensions.Hosting;

namespace WebCodeCli.Domain.Domain.Service;

public static class HostedServiceRuntimeMonitorPolicy
{
    public const string FeishuWebSocketHostedServiceType = "FeishuNetSdk.WebSocket.WssService";

    public static bool ShouldTrackExecuteTask(IHostedService hostedService)
    {
        ArgumentNullException.ThrowIfNull(hostedService);

        return ShouldTrackExecuteTask(
            hostedService.GetType().FullName,
            hostedService is BackgroundService);
    }

    public static bool ShouldTrackExecuteTask(string? hostedServiceTypeFullName, bool isBackgroundService)
    {
        return isBackgroundService
            && !string.Equals(hostedServiceTypeFullName, FeishuWebSocketHostedServiceType, StringComparison.Ordinal);
    }
}
