using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class ReplyTtsLocalServiceManagerTests
{
    [Fact]
    public async Task EnsureStartedAsync_WhenHealthProbeNeedsMoreThanTwoSeconds_ReturnsHealthyWithinStartupWindow()
    {
        using var harness = new Harness(
            new DelayedHealthSherpaKokoroTtsClient(
                unavailableProbeCount: 2,
                delayedHealthyResponse: TimeSpan.FromMilliseconds(2300)),
            startupTimeoutSeconds: 6);

        var result = await harness.Manager.EnsureStartedAsync(
            new FeishuReplyTtsHealthStatus
            {
                IsAvailable = true,
                StorageRoot = harness.StorageRoot,
                Message = "storage ok"
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal("ok", result.ServiceStatus);
        Assert.True(harness.TtsClient.HealthCallCount >= 3);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly string _testRoot;
        private readonly string _pidFilePath;

        public Harness(DelayedHealthSherpaKokoroTtsClient ttsClient, int startupTimeoutSeconds)
        {
            _testRoot = Path.Combine(
                Path.GetTempPath(),
                "ReplyTtsLocalServiceManagerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);

            StorageRoot = Path.Combine(_testRoot, "storage");
            Directory.CreateDirectory(StorageRoot);

            _pidFilePath = Path.Combine(_testRoot, "service.pid");
            var startScriptPath = CreateStartScript(_pidFilePath);

            TtsClient = ttsClient;
            var services = new ServiceCollection();
            services.AddScoped<ISherpaKokoroTtsClient>(_ => TtsClient);
            _serviceProvider = services.BuildServiceProvider();

            Manager = new ReplyTtsLocalServiceManager(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<FeishuReplyTtsOptions>(
                    new FeishuReplyTtsOptions
                    {
                        TtsServiceBaseUrl = "http://127.0.0.1:5058",
                        TtsServiceStartScriptPath = startScriptPath,
                        TtsServiceStartupTimeoutSeconds = startupTimeoutSeconds
                    }),
                NullLogger<ReplyTtsLocalServiceManager>.Instance);
        }

        public ReplyTtsLocalServiceManager Manager { get; }

        public string StorageRoot { get; }

        public DelayedHealthSherpaKokoroTtsClient TtsClient { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_pidFilePath) &&
                    int.TryParse(File.ReadAllText(_pidFilePath).Trim(), out var pid))
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(5000);
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            finally
            {
                _serviceProvider.Dispose();

                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, recursive: true);
                }
            }
        }

        private string CreateStartScript(string pidFilePath)
        {
            if (OperatingSystem.IsWindows())
            {
                var scriptPath = Path.Combine(_testRoot, "start.ps1");
                var escapedPidFilePath = pidFilePath.Replace("'", "''", StringComparison.Ordinal);
                File.WriteAllText(
                    scriptPath,
                    $$"""
                    param(
                        [string]$StorageRoot,
                        [int]$Port,
                        [string]$DefaultVoiceId,
                        [string]$Provider,
                        [string]$Python
                    )

                    Set-Content -Path '{{escapedPidFilePath}}' -Value $PID
                    Start-Sleep -Seconds 20
                    """);
                return scriptPath;
            }

            var shellScriptPath = Path.Combine(_testRoot, "start.sh");
            File.WriteAllText(
                shellScriptPath,
                $$"""
                #!/usr/bin/env bash
                echo $$ > "{{pidFilePath}}"
                sleep 20
                """);
            return shellScriptPath;
        }
    }

    private sealed class DelayedHealthSherpaKokoroTtsClient : ISherpaKokoroTtsClient
    {
        private readonly int _unavailableProbeCount;
        private readonly TimeSpan _delayedHealthyResponse;
        private int _healthCallCount;

        public DelayedHealthSherpaKokoroTtsClient(int unavailableProbeCount, TimeSpan delayedHealthyResponse)
        {
            _unavailableProbeCount = unavailableProbeCount;
            _delayedHealthyResponse = delayedHealthyResponse;
        }

        public int HealthCallCount => _healthCallCount;

        public async Task<FeishuReplyTtsHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _healthCallCount);
            if (callNumber <= _unavailableProbeCount)
            {
                throw new HttpRequestException("connection refused");
            }

            await Task.Delay(_delayedHealthyResponse, cancellationToken);
            return new FeishuReplyTtsHealthStatus
            {
                IsAvailable = true,
                ServiceStatus = "ok",
                Message = "healthy"
            };
        }

        public Task<IReadOnlyList<FeishuReplyTtsVoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
