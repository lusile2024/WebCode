using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.CliToolEnv;
using WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

namespace WebCodeCli.Domain.Tests;

public class CliToolEnvironmentServiceTests
{
    [Fact]
    public async Task GetEnvironmentVariablesAsync_WhenUserOverrideIsEmpty_RemovesInheritedValue()
    {
        const string toolId = "codex";
        const string username = "alice";

        var service = CreateService(
            new CliToolsOption
            {
                Tools =
                [
                    new CliToolConfig
                    {
                        Id = toolId,
                        EnvironmentVariables = new Dictionary<string, string>
                        {
                            ["BASE_KEY"] = "base-value",
                            ["REMOVE_ME"] = "tool-value"
                        }
                    }
                ]
            },
            new FakeCliToolEnvironmentVariableRepository(new Dictionary<string, Dictionary<string, string>>
            {
                [toolId] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["REMOVE_ME"] = "shared-value",
                    ["SHARED_KEY"] = "shared-only"
                }
            }),
            new FakeUserCliToolEnvironmentVariableRepository(new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
            {
                [username] = new(StringComparer.OrdinalIgnoreCase)
                {
                    [toolId] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["REMOVE_ME"] = string.Empty,
                        ["USER_KEY"] = "user-value"
                    }
                }
            }),
            new FakeUserContextService(username));

        var result = await service.GetEnvironmentVariablesAsync(toolId, username);

        Assert.Equal("base-value", result["BASE_KEY"]);
        Assert.Equal("shared-only", result["SHARED_KEY"]);
        Assert.Equal("user-value", result["USER_KEY"]);
        Assert.False(result.ContainsKey("REMOVE_ME"));
    }

    [Fact]
    public async Task SaveEnvironmentVariablesAsync_WhenInheritedVariableIsRemoved_PersistsDeletionMarker()
    {
        const string toolId = "claude-code";
        const string username = "alice";

        var sharedRepository = new FakeCliToolEnvironmentVariableRepository(new Dictionary<string, Dictionary<string, string>>
        {
            [toolId] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SHARED_KEY"] = "shared-value"
            }
        });
        var userRepository = new FakeUserCliToolEnvironmentVariableRepository();
        var service = CreateService(
            new CliToolsOption
            {
                Tools =
                [
                    new CliToolConfig
                    {
                        Id = toolId,
                        EnvironmentVariables = new Dictionary<string, string>
                        {
                            ["DEFAULT_KEY"] = "default-value",
                            ["KEEP_KEY"] = "default-keep"
                        }
                    }
                ]
            },
            sharedRepository,
            userRepository,
            new FakeUserContextService(username));

        var success = await service.SaveEnvironmentVariablesAsync(toolId, new Dictionary<string, string>
        {
            ["KEEP_KEY"] = "custom-value",
            ["USER_KEY"] = "user-value"
        }, username);

        Assert.True(success);

        var persisted = await userRepository.GetEnvironmentVariablesAsync(username, toolId);
        Assert.Equal(string.Empty, persisted["DEFAULT_KEY"]);
        Assert.Equal(string.Empty, persisted["SHARED_KEY"]);
        Assert.Equal("custom-value", persisted["KEEP_KEY"]);
        Assert.Equal("user-value", persisted["USER_KEY"]);

        var effective = await service.GetEnvironmentVariablesAsync(toolId, username);
        Assert.False(effective.ContainsKey("DEFAULT_KEY"));
        Assert.False(effective.ContainsKey("SHARED_KEY"));
        Assert.Equal("custom-value", effective["KEEP_KEY"]);
        Assert.Equal("user-value", effective["USER_KEY"]);
    }

    [Fact]
    public async Task GetEnvironmentVariablesAsync_WhenExplicitUsernameProvided_UsesExplicitUsernameInsteadOfContext()
    {
        const string toolId = "codex";
        var service = CreateService(
            new CliToolsOption(),
            new FakeCliToolEnvironmentVariableRepository(),
            new FakeUserCliToolEnvironmentVariableRepository(new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["luhaiyan"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    [toolId] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["API_KEY"] = "luhaiyan-key"
                    }
                },
                ["test"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    [toolId] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["API_KEY"] = "test-key"
                    }
                }
            }),
            new FakeUserContextService("luhaiyan"));

        var result = await service.GetEnvironmentVariablesAsync(toolId, "test");

        Assert.Equal("test-key", result["API_KEY"]);
    }

    private static CliToolEnvironmentService CreateService(
        CliToolsOption options,
        ICliToolEnvironmentVariableRepository repository,
        IUserCliToolEnvironmentVariableRepository userRepository,
        IUserContextService userContextService)
    {
        return new CliToolEnvironmentService(
            NullLogger<CliToolEnvironmentService>.Instance,
            Options.Create(options),
            repository,
            userRepository,
            userContextService);
    }

    private sealed class FakeUserContextService(string username) : IUserContextService
    {
        private string _username = username;

        public string GetCurrentUsername() => _username;

        public string GetCurrentRole() => "user";

        public bool IsAuthenticated() => true;

        public void SetCurrentUsername(string username)
        {
            _username = username;
        }
    }

    private sealed class FakeCliToolEnvironmentVariableRepository : Repository<CliToolEnvironmentVariable>, ICliToolEnvironmentVariableRepository
    {
        private readonly Dictionary<string, Dictionary<string, string>> _storage;

        public FakeCliToolEnvironmentVariableRepository(Dictionary<string, Dictionary<string, string>>? storage = null)
        {
            _storage = storage ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<Dictionary<string, string>> GetEnvironmentVariablesByToolIdAsync(string toolId)
        {
            if (_storage.TryGetValue(toolId, out var envVars))
            {
                return Task.FromResult(new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase));
            }

            return Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        public Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars)
        {
            _storage[toolId] = new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteByToolIdAsync(string toolId)
        {
            _storage.Remove(toolId);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeUserCliToolEnvironmentVariableRepository : Repository<UserCliToolEnvironmentVariableEntity>, IUserCliToolEnvironmentVariableRepository
    {
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _storage;

        public FakeUserCliToolEnvironmentVariableRepository(Dictionary<string, Dictionary<string, Dictionary<string, string>>>? storage = null)
        {
            _storage = storage ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string username, string toolId)
        {
            if (_storage.TryGetValue(username, out var toolMap) &&
                toolMap.TryGetValue(toolId, out var envVars))
            {
                return Task.FromResult(new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase));
            }

            return Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        public Task<bool> SaveEnvironmentVariablesAsync(string username, string toolId, Dictionary<string, string> envVars)
        {
            if (!_storage.TryGetValue(username, out var toolMap))
            {
                toolMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                _storage[username] = toolMap;
            }

            toolMap[toolId] = new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteByToolIdAsync(string username, string toolId)
        {
            if (_storage.TryGetValue(username, out var toolMap))
            {
                toolMap.Remove(toolId);
                if (toolMap.Count == 0)
                {
                    _storage.Remove(username);
                }
            }

            return Task.FromResult(true);
        }
    }
}
