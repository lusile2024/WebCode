using FeishuNetSdk;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IUserFeishuBotRuntimeService), ServiceLifetime.Singleton)]
public sealed class UserFeishuBotRuntimeService : IUserFeishuBotRuntimeService, IHostedService, IDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserFeishuBotRuntimeService> _logger;
    private readonly Dictionary<string, RuntimeEntry> _entriesByUsername = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _usernameByAppId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserFeishuBotRuntimeStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private bool _disposed;

    public UserFeishuBotRuntimeService(
        IServiceProvider rootServiceProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<UserFeishuBotRuntimeService> logger)
    {
        _rootServiceProvider = rootServiceProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<UserFeishuBotRuntimeStatus> GetStatusAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername == null)
        {
            return CreateStatus(string.Empty, null, UserFeishuBotRuntimeState.NotConfigured, false, false, "用户名不能为空。");
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            RefreshCompletedRuntime_NoLock(normalizedUsername);
            var config = await GetConfigAsync(normalizedUsername, cancellationToken);
            return BuildStatus_NoLock(normalizedUsername, config);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<UserFeishuBotRuntimeStatus> StartAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername == null)
        {
            return CreateStatus(string.Empty, null, UserFeishuBotRuntimeState.NotConfigured, false, false, "用户名不能为空。");
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            RefreshCompletedRuntime_NoLock(normalizedUsername);

            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();
            var config = await configService.GetByUsernameAsync(normalizedUsername);
            var defaults = configService.GetSharedDefaults();
            var options = UserFeishuBotOptionsFactory.CreateEffectiveOptions(defaults, config);

            if (!defaults.Enabled)
            {
                var disabledStatus = CreateStatus(
                    normalizedUsername,
                    config?.AppId,
                    UserFeishuBotRuntimeState.Failed,
                    UserFeishuBotOptionsFactory.HasUsableCredentials(config),
                    false,
                    "系统已禁用飞书渠道，当前无法启动机器人。");
                disabledStatus.LastError = disabledStatus.Message;
                _statusCache[normalizedUsername] = disabledStatus;
                return disabledStatus;
            }

            if (!_entriesByUsername.TryGetValue(normalizedUsername, out var existingEntry))
            {
                if (options == null)
                {
                    var status = CreateStatus(
                        normalizedUsername,
                        config?.AppId,
                        UserFeishuBotRuntimeState.NotConfigured,
                        UserFeishuBotOptionsFactory.HasUsableCredentials(config),
                        false,
                        "当前用户尚未配置可启动的飞书机器人。");
                    _statusCache[normalizedUsername] = status;
                    return status;
                }

                if (_usernameByAppId.TryGetValue(options.AppId, out var ownerUsername)
                    && !string.Equals(ownerUsername, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    var status = CreateStatus(
                        normalizedUsername,
                        options.AppId,
                        UserFeishuBotRuntimeState.Failed,
                        true,
                        false,
                        $"AppId {options.AppId} 已被用户 {ownerUsername} 启动。");
                    status.LastError = status.Message;
                    _statusCache[normalizedUsername] = status;
                    return status;
                }

                var startingStatus = CreateStatus(
                    normalizedUsername,
                    options.AppId,
                    UserFeishuBotRuntimeState.Starting,
                    true,
                    false,
                    $"正在启动飞书机器人 {options.AppId}。");
                startingStatus.LastStartedAt = _statusCache.TryGetValue(normalizedUsername, out var cachedBeforeStart)
                    ? cachedBeforeStart.LastStartedAt
                    : null;
                _statusCache[normalizedUsername] = startingStatus;

                RuntimeEntry entry;
                try
                {
                    entry = CreateRuntimeEntry(options);
                    _entriesByUsername[normalizedUsername] = entry;
                    _usernameByAppId[options.AppId] = normalizedUsername;
                }
                catch (Exception ex)
                {
                    var failedStatus = CreateStatus(
                        normalizedUsername,
                        options.AppId,
                        UserFeishuBotRuntimeState.Failed,
                        true,
                        true,
                        $"初始化飞书机器人失败: {ex.Message}");
                    failedStatus.LastError = failedStatus.Message;
                    _statusCache[normalizedUsername] = failedStatus;
                    _logger.LogError(ex, "初始化飞书机器人失败: user={Username}, appId={AppId}", normalizedUsername, options.AppId);
                    return failedStatus;
                }

                try
                {
                    await entry.HostedService.StartAsync(cancellationToken);
                    entry.AttachMonitor(task => OnRuntimeCompleted(normalizedUsername, options.AppId, task));
                    if (entry.ExecuteTask == null)
                    {
                        _logger.LogInformation(
                            "Skipping ExecuteTask monitoring for hosted service: user={Username}, appId={AppId}, type={HostedServiceType}",
                            normalizedUsername,
                            options.AppId,
                            entry.HostedService.GetType().FullName);
                    }
                }
                catch (Exception ex)
                {
                    CleanupEntry_NoLock(normalizedUsername, disposeEntry: true);
                    var failedStatus = CreateStatus(
                        normalizedUsername,
                        options.AppId,
                        UserFeishuBotRuntimeState.Failed,
                        true,
                        true,
                        $"启动飞书机器人失败: {ex.Message}");
                    failedStatus.LastError = failedStatus.Message;
                    _statusCache[normalizedUsername] = failedStatus;
                    _logger.LogError(ex, "启动飞书机器人失败: user={Username}, appId={AppId}", normalizedUsername, options.AppId);
                    return failedStatus;
                }

                var connectedStatus = CreateStatus(
                    normalizedUsername,
                    options.AppId,
                    UserFeishuBotRuntimeState.Connected,
                    true,
                    false,
                    $"飞书机器人 {options.AppId} 已连接。");
                connectedStatus.LastStartedAt = DateTime.Now;
                _statusCache[normalizedUsername] = connectedStatus;
                _logger.LogInformation("飞书机器人已启动: user={Username}, appId={AppId}", normalizedUsername, options.AppId);
                return connectedStatus;
            }

            return BuildStatus_NoLock(normalizedUsername, config);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<UserFeishuBotRuntimeStatus> StopAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername == null)
        {
            return CreateStatus(string.Empty, null, UserFeishuBotRuntimeState.NotConfigured, false, false, "用户名不能为空。");
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return await StopInternalAsync(normalizedUsername, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        string[] usernames;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            usernames = _entriesByUsername.Keys.ToArray();
        }
        finally
        {
            _mutex.Release();
        }

        foreach (var username in usernames)
        {
            await StopAsync(username, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutex.Dispose();

        foreach (var entry in _entriesByUsername.Values.ToList())
        {
            entry.Dispose();
        }

        _entriesByUsername.Clear();
        _usernameByAppId.Clear();
    }

    private async Task<UserFeishuBotRuntimeStatus> StopInternalAsync(string username, CancellationToken cancellationToken)
    {
        RefreshCompletedRuntime_NoLock(username);

        if (!_entriesByUsername.TryGetValue(username, out var entry))
        {
            var config = await GetConfigAsync(username, cancellationToken);
            var status = BuildStatus_NoLock(username, config);
            if (status.State == UserFeishuBotRuntimeState.Connected || status.State == UserFeishuBotRuntimeState.Starting)
            {
                status.State = UserFeishuBotRuntimeState.Stopped;
            }

            _statusCache[username] = status;
            return status;
        }

        var stoppingStatus = CreateStatus(
            username,
            entry.AppId,
            UserFeishuBotRuntimeState.Stopping,
            true,
            false,
            $"正在停止飞书机器人 {entry.AppId}。");
        stoppingStatus.LastStartedAt = _statusCache.TryGetValue(username, out var cachedBeforeStop)
            ? cachedBeforeStop.LastStartedAt
            : null;
        _statusCache[username] = stoppingStatus;

        try
        {
            await entry.HostedService.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止飞书机器人时发生异常: user={Username}, appId={AppId}", username, entry.AppId);
        }

        CleanupEntry_NoLock(username, disposeEntry: true);

        var currentConfig = await GetConfigAsync(username, cancellationToken);
        var canStart = UserFeishuBotOptionsFactory.HasUsableCredentials(currentConfig);
        var stoppedStatus = CreateStatus(
            username,
            currentConfig?.AppId ?? entry.AppId,
            canStart ? UserFeishuBotRuntimeState.Stopped : UserFeishuBotRuntimeState.NotConfigured,
            canStart,
            canStart,
            canStart ? "飞书机器人已停止，可手动重新启动。" : "当前用户未配置可启动的飞书机器人。");
        stoppedStatus.LastStartedAt = _statusCache.TryGetValue(username, out var cachedAfterStop)
            ? cachedAfterStop.LastStartedAt
            : null;
        _statusCache[username] = stoppedStatus;
        _logger.LogInformation("飞书机器人已停止: user={Username}, appId={AppId}", username, stoppedStatus.AppId);
        return stoppedStatus;
    }

    private RuntimeEntry CreateRuntimeEntry(FeishuOptions options)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddHttpClient();
        services.AddFeishuNetSdk(feishuOptions =>
        {
            feishuOptions.AppId = options.AppId;
            feishuOptions.AppSecret = options.AppSecret;
            feishuOptions.EncryptKey = options.EncryptKey;
            feishuOptions.VerificationToken = options.VerificationToken;
            feishuOptions.EnableLogging = true;
        }).AddFeishuWebSocket();

        services.AddSingleton(_ => _rootServiceProvider.GetRequiredService<FeishuMessageHandler>());
        services.AddSingleton(_ => _rootServiceProvider.GetRequiredService<FeishuCardActionHandler>());

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().FirstOrDefault();
        if (hostedService == null)
        {
            provider.Dispose();
            throw new InvalidOperationException("未找到飞书 WebSocket HostedService。");
        }

        return new RuntimeEntry(options.AppId, provider, hostedService);
    }

    private async Task<UserFeishuBotConfigEntity?> GetConfigAsync(string username, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();
        return await configService.GetByUsernameAsync(username);
    }

    private UserFeishuBotRuntimeStatus BuildStatus_NoLock(
        string username,
        UserFeishuBotConfigEntity? config)
    {
        if (_statusCache.TryGetValue(username, out var cached))
        {
            cached.Username = username;
            cached.AppId = config?.AppId ?? cached.AppId;
            cached.IsConfigured = UserFeishuBotOptionsFactory.HasUsableCredentials(config);
            cached.CanStart = cached.State is not UserFeishuBotRuntimeState.Starting and not UserFeishuBotRuntimeState.Connected and not UserFeishuBotRuntimeState.Stopping
                && UserFeishuBotOptionsFactory.HasUsableCredentials(config);
            cached.UpdatedAt = DateTime.Now;
            return CloneStatus(cached);
        }

        var isConfigured = UserFeishuBotOptionsFactory.HasUsableCredentials(config);
        return CreateStatus(
            username,
            config?.AppId,
            isConfigured ? UserFeishuBotRuntimeState.Stopped : UserFeishuBotRuntimeState.NotConfigured,
            isConfigured,
            isConfigured,
            isConfigured ? "已配置飞书机器人，点击启动后建立连接。" : "当前用户尚未配置可启动的飞书机器人。");
    }

    private void RefreshCompletedRuntime_NoLock(string username)
    {
        if (!_entriesByUsername.TryGetValue(username, out var entry) || entry.ExecuteTask == null || !entry.ExecuteTask.IsCompleted)
        {
            return;
        }

        if (entry.ExecuteTask.IsFaulted)
        {
            CleanupEntry_NoLock(username, disposeEntry: true);
            var failedStatus = CreateStatus(
                username,
                entry.AppId,
                UserFeishuBotRuntimeState.Failed,
                true,
                true,
                $"飞书机器人连接已中断: {entry.ExecuteTask.Exception?.GetBaseException().Message}");
            failedStatus.LastError = entry.ExecuteTask.Exception?.GetBaseException().Message;
            failedStatus.LastStartedAt = _statusCache.TryGetValue(username, out var cachedBeforeFailure)
                ? cachedBeforeFailure.LastStartedAt
                : null;
            _statusCache[username] = failedStatus;
            return;
        }

        if (entry.ExecuteTask.IsCanceled || entry.ExecuteTask.IsCompleted)
        {
            CleanupEntry_NoLock(username, disposeEntry: true);
            var stoppedStatus = CreateStatus(
                username,
                entry.AppId,
                UserFeishuBotRuntimeState.Stopped,
                true,
                true,
                "飞书机器人连接已停止。");
            stoppedStatus.LastStartedAt = _statusCache.TryGetValue(username, out var cachedBeforeStop)
                ? cachedBeforeStop.LastStartedAt
                : null;
            _statusCache[username] = stoppedStatus;
        }
    }

    private void OnRuntimeCompleted(string username, string appId, Task task)
    {
        _mutex.Wait();
        try
        {
            if (!_entriesByUsername.ContainsKey(username))
            {
                return;
            }

            CleanupEntry_NoLock(username, disposeEntry: true);

            var failedStatus = CreateStatus(
                username,
                appId,
                task.IsFaulted ? UserFeishuBotRuntimeState.Failed : UserFeishuBotRuntimeState.Stopped,
                true,
                true,
                task.IsFaulted
                    ? $"飞书机器人连接已中断: {task.Exception?.GetBaseException().Message}"
                    : "飞书机器人连接已停止。");
            failedStatus.LastError = task.IsFaulted ? task.Exception?.GetBaseException().Message : null;
            failedStatus.LastStartedAt = _statusCache.TryGetValue(username, out var cached) ? cached.LastStartedAt : null;
            _statusCache[username] = failedStatus;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void CleanupEntry_NoLock(string username, bool disposeEntry)
    {
        if (!_entriesByUsername.Remove(username, out var entry))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.AppId))
        {
            _usernameByAppId.Remove(entry.AppId);
        }

        if (disposeEntry)
        {
            entry.Dispose();
        }
    }

    private static string? NormalizeUsername(string username)
    {
        return string.IsNullOrWhiteSpace(username) ? null : username.Trim();
    }

    private static UserFeishuBotRuntimeStatus CloneStatus(UserFeishuBotRuntimeStatus status)
    {
        return new UserFeishuBotRuntimeStatus
        {
            Username = status.Username,
            AppId = status.AppId,
            State = status.State,
            IsConfigured = status.IsConfigured,
            CanStart = status.CanStart,
            Message = status.Message,
            LastError = status.LastError,
            LastStartedAt = status.LastStartedAt,
            UpdatedAt = status.UpdatedAt
        };
    }

    private static UserFeishuBotRuntimeStatus CreateStatus(
        string username,
        string? appId,
        UserFeishuBotRuntimeState state,
        bool isConfigured,
        bool canStart,
        string message)
    {
        return new UserFeishuBotRuntimeStatus
        {
            Username = username,
            AppId = string.IsNullOrWhiteSpace(appId) ? null : appId.Trim(),
            State = state,
            IsConfigured = isConfigured,
            CanStart = canStart,
            Message = message,
            UpdatedAt = DateTime.Now
        };
    }

    private sealed class RuntimeEntry : IDisposable
    {
        public RuntimeEntry(string appId, ServiceProvider serviceProvider, IHostedService hostedService)
        {
            AppId = appId;
            ServiceProvider = serviceProvider;
            HostedService = hostedService;
            ShouldTrackExecuteTask = HostedServiceRuntimeMonitorPolicy.ShouldTrackExecuteTask(hostedService);
            ExecuteTask = ShouldTrackExecuteTask
                ? (hostedService as BackgroundService)?.ExecuteTask
                : null;
        }

        public string AppId { get; }
        public ServiceProvider ServiceProvider { get; }
        public IHostedService HostedService { get; }
        public bool ShouldTrackExecuteTask { get; }
        public Task? ExecuteTask { get; private set; }

        public void AttachMonitor(Action<Task> onCompleted)
        {
            if (!ShouldTrackExecuteTask)
            {
                ExecuteTask = null;
                return;
            }

            ExecuteTask = (HostedService as BackgroundService)?.ExecuteTask;
            if (ExecuteTask != null)
            {
                _ = ExecuteTask.ContinueWith(onCompleted, TaskScheduler.Default);
            }
        }

        public void Dispose()
        {
            ServiceProvider.Dispose();
        }
    }

}
