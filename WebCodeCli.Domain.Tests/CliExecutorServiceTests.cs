using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Diagnostics;
using System.Reflection;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Tests;

public class CliExecutorServiceTests
{
    [Fact]
    public void GetCliThreadId_WhenPersistedThreadIdMissing_RecoversImportedCodexThreadIdFromTitle()
    {
        const string sessionId = "session-imported";
        const string cliThreadId = "019d1338-0c3f-7eb3-ae2b-e4617eb7d24e";

        var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    Title = $"[Codex] {cliThreadId}",
                    WorkspacePath = @"D:\VSWorkshop\OpenDify",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);
        var serviceProvider = new NullServiceProvider(
            repository,
            new StubSessionOutputService());

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = []
            }),
            NullLogger<PersistentProcessManager>.Instance,
            serviceProvider,
            new StubChatSessionService(),
            new StubCliAdapterFactory(),
            new StubCcSwitchService());

        var resolvedThreadId = service.GetCliThreadId(sessionId);

        Assert.Equal(cliThreadId, resolvedThreadId);
        Assert.Equal(cliThreadId, repository.LastUpdatedCliThreadId);
        Assert.Equal(cliThreadId, repository.GetById(sessionId).CliThreadId);
    }

    [Fact]
    public void GetCliThreadId_WhenImportedTitleIsNotARealThreadId_DoesNotRecoverWrongValue()
    {
        const string sessionId = "session-imported-friendly-title";

        var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    Title = "[Codex] 修复 OpenDify 登录问题",
                    WorkspacePath = @"D:\VSWorkshop\OpenDify",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);
        var serviceProvider = new NullServiceProvider(
            repository,
            new StubSessionOutputService());

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = []
            }),
            NullLogger<PersistentProcessManager>.Instance,
            serviceProvider,
            new StubChatSessionService(),
            new StubCliAdapterFactory(),
            new StubCcSwitchService());

        var resolvedThreadId = service.GetCliThreadId(sessionId);

        Assert.Null(resolvedThreadId);
        Assert.Null(repository.LastUpdatedCliThreadId);
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenProcessTimesOut_ReturnsTimeoutChunkInsteadOfThrowing()
    {
        var tool = new CliToolConfig
        {
            Id = "timeout-tool",
            Name = "Timeout Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Start-Sleep -Seconds 3\"",
            TimeoutSeconds = 1,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory(),
            new StubCcSwitchService());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-timeout", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var timeoutChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains("执行超时", timeoutChunk.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenAdapterProvidesDetailedFailure_ReturnsUpstreamErrorMessage()
    {
        const string upstreamError = "unexpected status 402 Payment Required";

        var tool = new CliToolConfig
        {
            Id = "adapter-error-tool",
            Name = "Adapter Error Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Write-Output 'RETRY: Reconnecting... 1/5'; Write-Output 'ERROR: unexpected status 402 Payment Required'; exit 1\"",
            TimeoutSeconds = 10,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory(new StubErrorAdapter()),
            new StubCcSwitchService());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-adapter-error", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains(upstreamError, failureChunk.ErrorMessage);
        Assert.DoesNotContain("执行失败（退出码", failureChunk.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenCcSwitchManagedToolNotReady_ReturnsBlockingMessage()
    {
        var tool = new CliToolConfig
        {
            Id = "codex",
            Name = "Codex",
            Command = "codex",
            ArgumentTemplate = "exec {prompt}",
            TimeoutSeconds = 10,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var ccSwitchService = new StubCcSwitchService(new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["codex"] = new()
            {
                ToolId = "codex",
                ToolName = "Codex",
                IsManaged = true,
                IsLaunchReady = false,
                StatusMessage = "Codex 当前未在 cc-switch 中激活 Provider。"
            }
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory(),
            ccSwitchService);

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-cc-switch-blocked", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Equal("Codex 当前未在 cc-switch 中激活 Provider。", failureChunk.ErrorMessage);
    }

    [Fact]
    public async Task SyncSessionCcSwitchSnapshotAsync_WhenManagedSessionExists_CopiesLiveConfigAndPersistsSnapshot()
    {
        const string sessionId = "session-sync-snapshot";
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var workspacePath = Path.Combine(tempRoot, "workspace");
        var liveConfigDirectory = Path.Combine(tempRoot, "live");
        var liveConfigPath = Path.Combine(liveConfigDirectory, "config.toml");
        const string liveConfigContent = "model = \"gpt-5.4\"\nprovider = \"provider-a\"\n";

        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(liveConfigDirectory);
        await File.WriteAllTextAsync(liveConfigPath, liveConfigContent);

        try
        {
            var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    WorkspacePath = workspacePath,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = tempRoot,
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(repository, new StubSessionOutputService()),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService(new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase)
                {
                    ["codex"] = new()
                    {
                        ToolId = "codex",
                        ToolName = "Codex",
                        IsManaged = true,
                        IsLaunchReady = true,
                        ActiveProviderId = "provider-a",
                        ActiveProviderName = "Provider A",
                        ActiveProviderCategory = "custom",
                        LiveConfigPath = liveConfigPath,
                        StatusMessage = "Codex 已由 cc-switch 管理并可直接启动。"
                    }
                }));

            var snapshot = await service.SyncSessionCcSwitchSnapshotAsync(sessionId);
            var snapshotPath = Path.Combine(workspacePath, ".codex", "config.toml");

            Assert.NotNull(snapshot);
            Assert.True(File.Exists(snapshotPath));
            Assert.Equal(liveConfigContent, await File.ReadAllTextAsync(snapshotPath));

            var persistedSession = repository.GetById(sessionId);
            Assert.True(persistedSession.UsesCcSwitchSnapshot);
            Assert.Equal("codex", persistedSession.CcSwitchSnapshotToolId);
            Assert.Equal("provider-a", persistedSession.CcSwitchProviderId);
            Assert.Equal("Provider A", persistedSession.CcSwitchProviderName);
            Assert.Equal("custom", persistedSession.CcSwitchProviderCategory);
            Assert.Equal(liveConfigPath, persistedSession.CcSwitchLiveConfigPath);
            Assert.Equal(Path.Combine(".codex", "config.toml"), persistedSession.CcSwitchSnapshotRelativePath);
            Assert.NotNull(persistedSession.CcSwitchSnapshotSyncedAt);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SyncSessionCcSwitchSnapshotAsync_WhenExplicitlyResynced_RefreshesPinnedProvider()
    {
        const string sessionId = "session-resync-snapshot";
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var workspacePath = Path.Combine(tempRoot, "workspace");
        var liveConfigDirectory = Path.Combine(tempRoot, "live");
        var liveConfigPath = Path.Combine(liveConfigDirectory, "config.toml");
        const string firstConfigContent = "provider = \"provider-a\"\n";
        const string secondConfigContent = "provider = \"provider-b\"\n";

        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(liveConfigDirectory);
        await File.WriteAllTextAsync(liveConfigPath, firstConfigContent);

        try
        {
            var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    WorkspacePath = workspacePath,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);

            var ccSwitchService = new StubCcSwitchService(new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase)
            {
                ["codex"] = new()
                {
                    ToolId = "codex",
                    ToolName = "Codex",
                    IsManaged = true,
                    IsLaunchReady = true,
                    ActiveProviderId = "provider-a",
                    ActiveProviderName = "Provider A",
                    ActiveProviderCategory = "custom",
                    LiveConfigPath = liveConfigPath,
                    StatusMessage = "Codex 已由 cc-switch 管理并可直接启动。"
                }
            });

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = tempRoot,
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(repository, new StubSessionOutputService()),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                ccSwitchService);

            await service.SyncSessionCcSwitchSnapshotAsync(sessionId);

            await File.WriteAllTextAsync(liveConfigPath, secondConfigContent);
            ccSwitchService.SetToolStatus("codex", new CcSwitchToolStatus
            {
                ToolId = "codex",
                ToolName = "Codex",
                IsManaged = true,
                IsLaunchReady = true,
                ActiveProviderId = "provider-b",
                ActiveProviderName = "Provider B",
                ActiveProviderCategory = "official",
                LiveConfigPath = liveConfigPath,
                StatusMessage = "Codex 已由 cc-switch 管理并可直接启动。"
            });

            var refreshedSnapshot = await service.SyncSessionCcSwitchSnapshotAsync(sessionId);
            var snapshotPath = Path.Combine(workspacePath, ".codex", "config.toml");

            Assert.NotNull(refreshedSnapshot);
            Assert.Equal("Provider B", refreshedSnapshot.ProviderName);
            Assert.Equal(secondConfigContent, await File.ReadAllTextAsync(snapshotPath));

            var persistedSession = repository.GetById(sessionId);
            Assert.Equal("provider-b", persistedSession.CcSwitchProviderId);
            Assert.Equal("Provider B", persistedSession.CcSwitchProviderName);
            Assert.Equal("official", persistedSession.CcSwitchProviderCategory);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenManagedSessionHasPinnedSnapshot_ContinuesUsingPinnedProvider()
    {
        const string sessionId = "session-pinned-snapshot";
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var workspacePath = Path.Combine(tempRoot, "workspace");
        var snapshotPath = Path.Combine(workspacePath, ".codex", "config.toml");

        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        await File.WriteAllTextAsync(snapshotPath, "provider = \"provider-a\"\n");

        try
        {
            var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    WorkspacePath = workspacePath,
                    UsesCcSwitchSnapshot = true,
                    CcSwitchSnapshotToolId = "codex",
                    CcSwitchProviderId = "provider-a",
                    CcSwitchProviderName = "Provider A",
                    CcSwitchSnapshotRelativePath = Path.Combine(".codex", "config.toml"),
                    CcSwitchSnapshotSyncedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);

            var tool = new CliToolConfig
            {
                Id = "codex",
                Name = "Codex",
                Command = "powershell.exe",
                ArgumentTemplate = "-NoProfile -Command \"Write-Output 'pinned-ok'\"",
                TimeoutSeconds = 10,
                Enabled = true
            };

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = tempRoot,
                    Tools = [tool]
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(repository, new StubSessionOutputService()),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService(new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase)
                {
                    ["codex"] = new()
                    {
                        ToolId = "codex",
                        ToolName = "Codex",
                        IsManaged = true,
                        IsLaunchReady = false,
                        StatusMessage = "Codex 当前未在 cc-switch 中激活 Provider。"
                    }
                }));

            var chunks = new List<StreamOutputChunk>();
            await foreach (var chunk in service.ExecuteStreamAsync(sessionId, tool.Id, "ignored"))
            {
                chunks.Add(chunk);
            }

            Assert.DoesNotContain(chunks, c => c.IsError && c.IsCompleted);
            Assert.Contains(chunks, c => c.Content.Contains("pinned-ok", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenPinnedSnapshotFileIsMissing_ReturnsSyncRequiredMessage()
    {
        const string sessionId = "session-missing-snapshot";
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var workspacePath = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspacePath);

        try
        {
            var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    WorkspacePath = workspacePath,
                    UsesCcSwitchSnapshot = true,
                    CcSwitchSnapshotToolId = "codex",
                    CcSwitchProviderId = "provider-a",
                    CcSwitchProviderName = "Provider A",
                    CcSwitchSnapshotRelativePath = Path.Combine(".codex", "config.toml"),
                    CcSwitchSnapshotSyncedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);

            var tool = new CliToolConfig
            {
                Id = "codex",
                Name = "Codex",
                Command = "powershell.exe",
                ArgumentTemplate = "-NoProfile -Command \"Write-Output 'should-not-run'\"",
                TimeoutSeconds = 10,
                Enabled = true
            };

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = tempRoot,
                    Tools = [tool]
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(repository, new StubSessionOutputService()),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService());

            var chunks = new List<StreamOutputChunk>();
            await foreach (var chunk in service.ExecuteStreamAsync(sessionId, tool.Id, "ignored"))
            {
                chunks.Add(chunk);
            }

            var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
            Assert.Contains("当前会话固定的 Codex Provider 快照已缺失或不可用。", failureChunk.ErrorMessage);
            Assert.Contains("同步到当前 cc-switch Provider", failureChunk.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CodexAdapter_ParseOutputLine_WhenTurnFailed_SetsErrorMessage()
    {
        const string upstreamError = "unexpected status 402 Payment Required";
        var adapter = new CodexAdapter();

        var outputEvent = adapter.ParseOutputLine(
            """{"type":"turn.failed","error":{"message":"unexpected status 402 Payment Required","code":402}}""");

        var failureEvent = Assert.IsType<CliOutputEvent>(outputEvent);
        Assert.True(failureEvent.IsError);
        Assert.Equal(upstreamError, failureEvent.ErrorMessage);
    }

    [Fact]
    public void RewriteCodexLaunchToNode_WhenCommandIsCmdWrapper_RewritesToNodeJsEntry()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var npmRoot = Path.Combine(tempRoot, "npm");
        var codexCmdPath = Path.Combine(npmRoot, "codex.cmd");
        var codexJsPath = Path.Combine(npmRoot, "node_modules", "@openai", "codex", "bin", "codex.js");
        var fakeNodePath = Path.Combine(tempRoot, "node.exe");

        Directory.CreateDirectory(Path.GetDirectoryName(codexJsPath)!);
        File.WriteAllText(codexCmdPath, "@echo off");
        File.WriteAllText(codexJsPath, "console.log('codex');");
        File.WriteAllText(fakeNodePath, string.Empty);

        try
        {
            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService());

            typeof(CliExecutorService)
                .GetField("_preferredNodeExecutablePath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, fakeNodePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = codexCmdPath,
                Arguments = "exec --json \"problematic \\\"prompt\\\"\""
            };

            typeof(CliExecutorService)
                .GetMethod("RewriteCodexLaunchToNode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [startInfo, new CliToolConfig { Id = "codex", Command = "codex" }, codexCmdPath, startInfo.Arguments]);

            Assert.Equal(fakeNodePath, startInfo.FileName);
            Assert.StartsWith($"\"{codexJsPath}\" ", startInfo.Arguments, StringComparison.Ordinal);
            Assert.Contains("problematic \\\"prompt\\\"", startInfo.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildCodexConfigContent_UsesNewSchema()
    {
        var envVars = new Dictionary<string, string>
        {
            ["CODEX_BASE_URL"] = "https://api.routin.ai/v1",
            ["CODEX_MODEL"] = "gpt-5.4",
            ["CODEX_MODEL_PROVIDER"] = "meteor-ai",
            ["CODEX_PROVIDER_NAME"] = "meteor-ai",
            ["CODEX_WIRE_API"] = "responses",
            ["CODEX_APPROVAL_POLICY"] = "never",
            ["CODEX_MODEL_REASONING_EFFORT"] = "xhigh",
            ["CODEX_SANDBOX_MODE"] = "danger-full-access"
        };

        var configContent = (string)typeof(CliExecutorService)
            .GetMethod("BuildCodexConfigContent", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [envVars])!;

        Assert.Contains("model_provider = \"meteor-ai\"", configContent, StringComparison.Ordinal);
        Assert.Contains("disable_response_storage = true", configContent, StringComparison.Ordinal);
        Assert.Contains("max_context = 1000000", configContent, StringComparison.Ordinal);
        Assert.Contains("context_compact_limit = 800000", configContent, StringComparison.Ordinal);
        Assert.Contains("rmcp_client = true", configContent, StringComparison.Ordinal);
        Assert.Contains("model_verbosity = \"high\"", configContent, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.claude]", configContent, StringComparison.Ordinal);
        Assert.Contains("[model_providers.\"meteor-ai\"]", configContent, StringComparison.Ordinal);
        Assert.Contains("requires_openai_auth = true", configContent, StringComparison.Ordinal);
        Assert.Contains("[windows]", configContent, StringComparison.Ordinal);
        Assert.DoesNotContain("\nprofile = ", configContent, StringComparison.Ordinal);
        Assert.DoesNotContain("[profiles.", configContent, StringComparison.Ordinal);
        Assert.DoesNotContain("env_key =", configContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveCommandPath_WhenCommandExistsOnWindowsPath_ReturnsFullPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var pathEntry = Path.Combine(tempRoot, "bin");
        const string commandName = "webcode-path-only-test";
        var commandPath = Path.Combine(pathEntry, commandName + ".cmd");
        Directory.CreateDirectory(pathEntry);
        File.WriteAllText(commandPath, "@echo off");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

        try
        {
            Environment.SetEnvironmentVariable("PATH", pathEntry);
            Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService());

            var resolvedCommand = (string)typeof(CliExecutorService)
                .GetMethod("ResolveCommandPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [commandName])!;

            Assert.Equal(commandPath, resolvedCommand, ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenCommandExistsAsWindowsCmdShimOnPath_StartsSuccessfully()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shimDirectory = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shimDirectory);

        var shimPath = Path.Combine(shimDirectory, "fakecli.cmd");
        await File.WriteAllTextAsync(shimPath, "@ECHO off\r\necho shim-ok\r\n");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", shimDirectory + ";" + originalPath);

        try
        {
            var tool = new CliToolConfig
            {
                Id = "fakecli",
                Name = "Fake CLI",
                Command = "fakecli",
                ArgumentTemplate = "",
                TimeoutSeconds = 5,
                Enabled = true
            };

            var options = Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = [tool]
            });

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                options,
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory(),
                new StubCcSwitchService());

            var chunks = new List<StreamOutputChunk>();
            await foreach (var chunk in service.ExecuteStreamAsync("session-cmd-shim", tool.Id, "ignored"))
            {
                chunks.Add(chunk);
            }

            Assert.DoesNotContain(chunks, c => c.IsError && c.IsCompleted);
            Assert.Contains(chunks, c => c.Content.Contains("shim-ok", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);

            if (Directory.Exists(shimDirectory))
            {
                Directory.Delete(shimDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenWorkingDirectoryMissing_ReturnsFriendlyError()
    {
        var tool = new CliToolConfig
        {
            Id = "missing-working-dir-tool",
            Name = "Missing Working Dir Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Write-Output 'should not start'\"",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"), "missing"),
            TimeoutSeconds = 10,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory(),
            new StubCcSwitchService());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-missing-working-dir", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains("工作目录不存在", failureChunk.ErrorMessage);
        Assert.Contains(tool.WorkingDirectory, failureChunk.ErrorMessage);
    }

    private sealed class StubChatSessionService : IChatSessionService
    {
        public void AddMessage(string sessionId, ChatMessage message) { }

        public List<ChatMessage> GetMessages(string sessionId) => [];

        public void ClearSession(string sessionId) { }

        public void UpdateMessage(string sessionId, string messageId, Action<ChatMessage> updateAction) { }

        public ChatMessage? GetMessage(string sessionId, string messageId) => null;
    }

    private sealed class StubCliAdapterFactory(params ICliToolAdapter[] adapters) : ICliAdapterFactory
    {
        private readonly ICliToolAdapter[] _adapters = adapters;

        public ICliToolAdapter? GetAdapter(CliToolConfig tool)
            => _adapters.FirstOrDefault(adapter => adapter.CanHandle(tool));

        public ICliToolAdapter? GetAdapter(string toolId)
            => _adapters.FirstOrDefault(adapter =>
                adapter.SupportedToolIds.Any(id => string.Equals(id, toolId, StringComparison.OrdinalIgnoreCase)));

        public bool SupportsStreamParsing(CliToolConfig tool)
            => GetAdapter(tool)?.SupportsStreamParsing ?? false;

        public IEnumerable<ICliToolAdapter> GetAllAdapters() => _adapters;
    }

    private sealed class StubErrorAdapter : ICliToolAdapter
    {
        public string[] SupportedToolIds => ["adapter-error-tool"];

        public bool SupportsStreamParsing => true;

        public bool CanHandle(CliToolConfig tool)
            => string.Equals(tool.Id, "adapter-error-tool", StringComparison.OrdinalIgnoreCase);

        public string BuildArguments(CliToolConfig tool, string prompt, CliSessionContext context)
            => tool.ArgumentTemplate;

        public CliOutputEvent? ParseOutputLine(string line)
        {
            if (line.StartsWith("RETRY: ", StringComparison.OrdinalIgnoreCase))
            {
                return new CliOutputEvent
                {
                    EventType = "error",
                    IsError = true,
                    ErrorMessage = line["RETRY: ".Length..]
                };
            }

            if (line.StartsWith("ERROR: ", StringComparison.OrdinalIgnoreCase))
            {
                return new CliOutputEvent
                {
                    EventType = "error",
                    IsError = true,
                    ErrorMessage = line["ERROR: ".Length..]
                };
            }

            return null;
        }

        public string? ExtractSessionId(CliOutputEvent outputEvent) => null;

        public string? ExtractAssistantMessage(CliOutputEvent outputEvent) => null;

        public string GetEventTitle(CliOutputEvent outputEvent) => outputEvent.Title ?? string.Empty;

        public string GetEventBadgeClass(CliOutputEvent outputEvent) => string.Empty;

        public string GetEventBadgeLabel(CliOutputEvent outputEvent) => string.Empty;
    }

    private sealed class StubSessionOutputService : ISessionOutputService
    {
        public Task<OutputPanelState?> GetBySessionIdAsync(string sessionId)
            => Task.FromResult<OutputPanelState?>(new OutputPanelState
            {
                SessionId = sessionId,
                ActiveThreadId = string.Empty
            });

        public Task<bool> SaveAsync(OutputPanelState state) => Task.FromResult(true);

        public Task<bool> DeleteBySessionIdAsync(string sessionId) => Task.FromResult(true);
    }

    private sealed class StubCcSwitchService : ICcSwitchService
    {
        private readonly Dictionary<string, CcSwitchToolStatus> _statuses;

        public StubCcSwitchService(Dictionary<string, CcSwitchToolStatus>? statuses = null)
        {
            _statuses = statuses ?? new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsManagedTool(string toolId)
        {
            return string.Equals(toolId, "claude-code", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "opencode", StringComparison.OrdinalIgnoreCase);
        }

        public Task<CcSwitchStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CcSwitchStatus
            {
                IsDetected = true,
                Tools = new Dictionary<string, CcSwitchToolStatus>(_statuses, StringComparer.OrdinalIgnoreCase)
            });
        }

        public Task<CcSwitchToolStatus> GetToolStatusAsync(string toolId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_statuses.TryGetValue(toolId, out var status))
            {
                return Task.FromResult(CloneStatus(status));
            }

            return Task.FromResult(new CcSwitchToolStatus
            {
                ToolId = toolId,
                ToolName = toolId,
                IsManaged = IsManagedTool(toolId),
                IsLaunchReady = true,
                StatusMessage = "ok"
            });
        }

        public Task<IReadOnlyDictionary<string, CcSwitchToolStatus>> GetToolStatusesAsync(IEnumerable<string> toolIds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var toolId in toolIds)
            {
                result[toolId] = GetToolStatusAsync(toolId, cancellationToken).GetAwaiter().GetResult();
            }

            return Task.FromResult<IReadOnlyDictionary<string, CcSwitchToolStatus>>(result);
        }

        public void SetToolStatus(string toolId, CcSwitchToolStatus status)
        {
            _statuses[toolId] = CloneStatus(status);
        }

        private static CcSwitchToolStatus CloneStatus(CcSwitchToolStatus status)
        {
            return new CcSwitchToolStatus
            {
                ToolId = status.ToolId,
                ToolName = status.ToolName,
                AppType = status.AppType,
                IsManaged = status.IsManaged,
                IsDetected = status.IsDetected,
                HasDatabase = status.HasDatabase,
                HasSettingsFile = status.HasSettingsFile,
                HasLiveConfig = status.HasLiveConfig,
                UsesSettingsOverride = status.UsesSettingsOverride,
                IsLaunchReady = status.IsLaunchReady,
                ConfigFileName = status.ConfigFileName,
                ConfigDirectory = status.ConfigDirectory,
                DatabasePath = status.DatabasePath,
                SettingsPath = status.SettingsPath,
                LiveConfigPath = status.LiveConfigPath,
                ActiveProviderId = status.ActiveProviderId,
                ActiveProviderName = status.ActiveProviderName,
                ActiveProviderCategory = status.ActiveProviderCategory,
                StatusMessage = status.StatusMessage
            };
        }
    }

    private sealed class NullServiceProvider(
        IChatSessionRepository? chatSessionRepository = null,
        ISessionOutputService? sessionOutputService = null)
        : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            if (serviceType == typeof(IChatSessionRepository))
            {
                return chatSessionRepository;
            }

            if (serviceType == typeof(ISessionOutputService))
            {
                return sessionOutputService;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }

    private sealed class StubChatSessionRepository(IEnumerable<ChatSessionEntity> sessions) : IChatSessionRepository
    {
        private readonly List<ChatSessionEntity> _sessions = sessions.ToList();

        public string? LastUpdatedCliThreadId { get; private set; }

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatSessionEntity> GetList() => _sessions.ToList();
        public Task<List<ChatSessionEntity>> GetListAsync() => Task.FromResult(GetList());
        public List<ChatSessionEntity> GetList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Where(whereExpression).ToList();
        public Task<List<ChatSessionEntity>> GetListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetList(whereExpression));
        public int Count(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Count(whereExpression);
        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(Count(whereExpression));
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public ChatSessionEntity GetById(dynamic id) => _sessions.First(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase));
        public Task<ChatSessionEntity> GetByIdAsync(dynamic id) => Task.FromResult(GetById(id));
        public ChatSessionEntity GetSingle(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Single(whereExpression);
        public Task<ChatSessionEntity> GetSingleAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetSingle(whereExpression));
        public ChatSessionEntity GetFirst(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().First(whereExpression);
        public Task<ChatSessionEntity> GetFirstAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetFirst(whereExpression));
        public bool Insert(ChatSessionEntity obj) { _sessions.Add(obj); return true; }
        public Task<bool> InsertAsync(ChatSessionEntity obj) => Task.FromResult(Insert(obj));
        public bool InsertRange(List<ChatSessionEntity> objs) { _sessions.AddRange(objs); return true; }
        public Task<bool> InsertRangeAsync(List<ChatSessionEntity> objs) => Task.FromResult(InsertRange(objs));
        public int InsertReturnIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<int> InsertReturnIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public long InsertReturnBigIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<long> InsertReturnBigIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool DeleteByIds(dynamic[] ids) => throw new NotSupportedException();
        public Task<bool> DeleteByIdsAsync(dynamic[] ids) => throw new NotSupportedException();
        public bool Delete(dynamic id) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(dynamic id) => throw new NotSupportedException();
        public bool Delete(ChatSessionEntity obj) => _sessions.Remove(obj);
        public Task<bool> DeleteAsync(ChatSessionEntity obj) => Task.FromResult(Delete(obj));
        public bool Delete(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ChatSessionEntity obj)
        {
            var existing = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, obj.SessionId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _sessions.Add(obj);
                return true;
            }

            var index = _sessions.IndexOf(existing);
            _sessions[index] = obj;
            return true;
        }
        public Task<bool> InsertOrUpdateAsync(ChatSessionEntity obj) => Task.FromResult(InsertOrUpdate(obj));
        public Task<bool> UpdateRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool IsAny(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)) > 0);
        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.UpdatedAt).ToList());
        public Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> UpdateCliThreadIdAsync(string sessionId, string cliThreadId)
        {
            var session = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                return Task.FromResult(false);
            }

            session.CliThreadId = cliThreadId;
            session.UpdatedAt = DateTime.Now;
            LastUpdatedCliThreadId = cliThreadId;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace) => Task.FromResult(true);
        public Task<bool> UpdateSessionTitleAsync(string sessionId, string title) => Task.FromResult(true);
        public Task<bool> UpdateCcSwitchSnapshotAsync(string sessionId, CcSwitchSessionSnapshot snapshot)
        {
            var session = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                return Task.FromResult(false);
            }

            session.UsesCcSwitchSnapshot = snapshot.UsesSnapshot;
            session.CcSwitchSnapshotToolId = snapshot.ToolId;
            session.CcSwitchProviderId = snapshot.ProviderId;
            session.CcSwitchProviderName = snapshot.ProviderName;
            session.CcSwitchProviderCategory = snapshot.ProviderCategory;
            session.CcSwitchLiveConfigPath = snapshot.SourceLiveConfigPath;
            session.CcSwitchSnapshotRelativePath = snapshot.SnapshotRelativePath;
            session.CcSwitchSnapshotSyncedAt = snapshot.SyncedAt;
            session.UpdatedAt = DateTime.Now;
            return Task.FromResult(true);
        }
        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult(new List<ChatSessionEntity>());
        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult<ChatSessionEntity?>(null);
        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null) => Task.FromResult(Guid.NewGuid().ToString("N"));
    }
}
