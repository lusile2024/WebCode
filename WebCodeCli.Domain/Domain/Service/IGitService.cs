using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// Git 服务接口
/// </summary>
public interface IGitService
{
    /// <summary>
    /// 检测工作区是否为 Git 仓库
    /// </summary>
    bool IsGitRepository(string workspacePath);
    
    /// <summary>
    /// 获取文件的提交历史
    /// </summary>
    Task<List<GitCommit>> GetFileHistoryAsync(string workspacePath, string filePath, int maxCount = 50);
    
    /// <summary>
    /// 获取特定版本的文件内容
    /// </summary>
    Task<string> GetFileContentAtCommitAsync(string workspacePath, string filePath, string commitHash);
    
    /// <summary>
    /// 获取文件差异
    /// </summary>
    Task<GitDiffResult> GetFileDiffAsync(string workspacePath, string filePath, string fromCommit, string toCommit);
    
    /// <summary>
    /// 获取工作区状态
    /// </summary>
    Task<GitStatus> GetWorkspaceStatusAsync(string workspacePath);
    
    /// <summary>
    /// 获取所有提交历史
    /// </summary>
    Task<List<GitCommit>> GetAllCommitsAsync(string workspacePath, int maxCount = 100);
    
    /// <summary>
    /// 克隆远程仓库
    /// </summary>
    /// <param name="gitUrl">Git 仓库地址</param>
    /// <param name="localPath">本地目标路径</param>
    /// <param name="branch">分支名称</param>
    /// <param name="credentials">认证凭据</param>
    /// <param name="progress">进度回调</param>
    /// <returns>是否成功</returns>
    Task<(bool Success, string? ErrorMessage)> CloneAsync(
        string gitUrl, 
        string localPath, 
        string branch, 
        GitCredentials? credentials = null,
        Action<CloneProgress>? progress = null);
    
    /// <summary>
    /// 拉取远程更新
    /// </summary>
    /// <param name="localPath">本地仓库路径</param>
    /// <param name="credentials">认证凭据</param>
    /// <returns>是否成功</returns>
    Task<(bool Success, string? ErrorMessage)> PullAsync(
        string localPath, 
        GitCredentials? credentials = null);
    
    /// <summary>
    /// 获取远程仓库的分支列表
    /// </summary>
    /// <param name="gitUrl">Git 仓库地址</param>
    /// <param name="credentials">认证凭据</param>
    /// <returns>分支名称列表</returns>
    Task<(List<string> Branches, string? ErrorMessage)> ListRemoteBranchesAsync(
        string gitUrl, 
        GitCredentials? credentials = null);
    
    /// <summary>
    /// 获取本地仓库的当前分支
    /// </summary>
    /// <param name="localPath">本地仓库路径</param>
    /// <returns>当前分支名称</returns>
    string? GetCurrentBranch(string localPath);

    /// <summary>
    /// 切换本地仓库分支
    /// </summary>
    /// <param name="localPath">本地仓库路径</param>
    /// <param name="branch">目标分支名称</param>
    /// <param name="credentials">认证凭据</param>
    Task<(bool Success, string? ErrorMessage)> SwitchBranchAsync(
        string localPath,
        string branch,
        GitCredentials? credentials = null);
}
