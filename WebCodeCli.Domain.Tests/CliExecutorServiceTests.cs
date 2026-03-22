using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;

namespace WebCodeCli.Domain.Tests;

public class CliExecutorServiceTests
{
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
            new StubCliAdapterFactory());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-timeout", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var timeoutChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains("执行超时", timeoutChunk.ErrorMessage);
    }

    private sealed class StubChatSessionService : IChatSessionService
    {
        public void AddMessage(string sessionId, ChatMessage message) { }

        public List<ChatMessage> GetMessages(string sessionId) => [];

        public void ClearSession(string sessionId) { }

        public void UpdateMessage(string sessionId, string messageId, Action<ChatMessage> updateAction) { }

        public ChatMessage? GetMessage(string sessionId, string messageId) => null;
    }

    private sealed class StubCliAdapterFactory : ICliAdapterFactory
    {
        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => null;

        public ICliToolAdapter? GetAdapter(string toolId) => null;

        public bool SupportsStreamParsing(CliToolConfig tool) => false;

        public IEnumerable<ICliToolAdapter> GetAllAdapters() => [];
    }

    private sealed class NullServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }
}
