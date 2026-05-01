using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

public interface ICodexThreadProviderSyncService
{
    Task<CodexThreadProviderSyncResult> SyncThreadProviderAsync(
        CodexThreadProviderSyncRequest request,
        CancellationToken cancellationToken = default);
}
