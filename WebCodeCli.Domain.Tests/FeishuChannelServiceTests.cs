using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Linq.Expressions;
using System.Reflection;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Repositories.Base;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Tests;

public class FeishuChannelServiceTests
{
    [Fact]
    public void CreateNewSession_WithCustomWorkspace_PersistsWorkspacePath()
    {
        var repository = CreateRepository(out var repositoryProxy);
        var sessionDirectoryService = new RecordingSessionDirectoryService(repositoryProxy);
        var serviceProvider = new TestServiceProvider(repository, sessionDirectoryService);

        var service = new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            CreateProxy<IFeishuCardKitClient>(),
            serviceProvider,
            CreateProxy<ICliExecutorService>(),
            CreateProxy<IChatSessionService>());

        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-custom-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        try
        {
            var sessionId = service.CreateNewSession(
                new FeishuIncomingMessage
                {
                    ChatId = "oc_custom_workspace_chat",
                    SenderName = "luhaiyan"
                },
                workspacePath,
                "codex");

            var stored = repositoryProxy.GetStored(sessionId);

            Assert.Equal(workspacePath, stored.WorkspacePath);
            Assert.True(stored.IsCustomWorkspace);
            Assert.Equal("codex", stored.ToolId);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private static IChatSessionRepository CreateRepository(out ChatSessionRepositoryProxy proxy)
    {
        var repository = DispatchProxy.Create<IChatSessionRepository, ChatSessionRepositoryProxy>();
        proxy = (ChatSessionRepositoryProxy)(object)repository;
        return repository;
    }

    private static T CreateProxy<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultInterfaceProxy<T>>();
    }

    private sealed class TestServiceProvider(
        IChatSessionRepository chatSessionRepository,
        ISessionDirectoryService sessionDirectoryService) : IServiceProvider, IServiceScopeFactory, IServiceScope
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

            if (serviceType == typeof(ISessionDirectoryService))
            {
                return sessionDirectoryService;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSessionDirectoryService(ChatSessionRepositoryProxy repository) : ISessionDirectoryService
    {
        public Task SetSessionWorkspaceAsync(string sessionId, string username, string directoryPath, bool isCustom = true)
        {
            var session = repository.GetStored(sessionId);
            session.WorkspacePath = directoryPath;
            session.IsCustomWorkspace = isCustom;
            session.UpdatedAt = DateTime.Now;
            repository.Store(session);
            return Task.CompletedTask;
        }

        public Task<string?> GetSessionWorkspaceAsync(string sessionId, string username)
            => Task.FromResult<string?>(repository.GetStored(sessionId).WorkspacePath);

        public Task SwitchSessionWorkspaceAsync(string sessionId, string username, string newDirectoryPath)
            => SetSessionWorkspaceAsync(sessionId, username, newDirectoryPath);

        public Task<bool> VerifySessionWorkspacePermissionAsync(string sessionId, string username, string requiredPermission = "write")
            => Task.FromResult(true);

        public Task<List<object>> GetUserAccessibleDirectoriesAsync(string username)
            => Task.FromResult(new List<object>());

        public Task<AllowedDirectoryBrowseResult> BrowseAllowedDirectoriesAsync(string? path, string? username = null)
            => throw new NotSupportedException();
    }

    private class ChatSessionRepositoryProxy : DispatchProxy
    {
        private readonly Dictionary<string, ChatSessionEntity> _sessions = new(StringComparer.OrdinalIgnoreCase);

        public ChatSessionEntity GetStored(string sessionId)
        {
            return Clone(_sessions[sessionId]);
        }

        public void Store(ChatSessionEntity session)
        {
            _sessions[session.SessionId] = Clone(session);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IChatSessionRepository.CreateFeishuSessionAsync):
                {
                    var chatKey = (string)args![0]!;
                    var username = (string)args[1]!;
                    var workspacePath = args[2] as string;
                    var toolId = args[3] as string;
                    var sessionId = Guid.NewGuid().ToString();

                    _sessions[sessionId] = new ChatSessionEntity
                    {
                        SessionId = sessionId,
                        Username = username,
                        FeishuChatKey = chatKey,
                        IsFeishuActive = true,
                        WorkspacePath = workspacePath,
                        ToolId = toolId,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    return Task.FromResult(sessionId);
                }
                case nameof(IRepository<ChatSessionEntity>.GetByIdAsync):
                {
                    var sessionId = args![0]?.ToString() ?? string.Empty;
                    var session = _sessions.TryGetValue(sessionId, out var stored) ? Clone(stored) : null;
                    return Task.FromResult(session);
                }
                case nameof(IRepository<ChatSessionEntity>.UpdateAsync):
                {
                    var session = Clone((ChatSessionEntity)args![0]!);
                    _sessions[session.SessionId] = session;
                    return Task.FromResult(true);
                }
            }

            return GetDefaultReturnValue(targetMethod?.ReturnType);
        }

        private static object? GetDefaultReturnValue(Type? returnType)
        {
            if (returnType == null || returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [defaultValue]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }

        private static ChatSessionEntity Clone(ChatSessionEntity session)
        {
            return new ChatSessionEntity
            {
                SessionId = session.SessionId,
                Username = session.Username,
                Title = session.Title,
                WorkspacePath = session.WorkspacePath,
                ToolId = session.ToolId,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt,
                IsWorkspaceValid = session.IsWorkspaceValid,
                ProjectId = session.ProjectId,
                FeishuChatKey = session.FeishuChatKey,
                IsFeishuActive = session.IsFeishuActive,
                IsCustomWorkspace = session.IsCustomWorkspace
            };
        }
    }

    private class DefaultInterfaceProxy<T> : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [defaultValue]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
