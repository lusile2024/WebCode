using Microsoft.Extensions.Options;
using System.Reflection;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class ReplyTtsStorageRootResolverTests
{
    [Fact]
    public void Resolve_WhenExplicitStorageRootIsSet_AlwaysUsesConfiguredRoot()
    {
        var options = CreateMonitor(new FeishuReplyTtsOptions
        {
            TtsStorageRoot = @"E:\custom-melotts",
            AllowSystemDriveInstall = false
        });
        var resolver = new ReplyTtsStorageRootResolver(
            options,
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.Equal(@"E:\custom-melotts", result.StorageRoot);
        Assert.Equal(@"E:\custom-melotts\models", result.ModelsRoot);
        Assert.Equal(@"E:\custom-melotts\cache", result.CacheRoot);
        Assert.Equal(@"E:\custom-melotts\temp", result.TempRoot);
        Assert.Equal(@"E:\custom-melotts\logs", result.LogsRoot);
        Assert.Equal(@"E:\custom-melotts\venv", result.VenvRoot);
    }

    [Fact]
    public void Resolve_WhenExplicitStorageRootIsWindowsDriveOnly_NormalizesToDriveRoot()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions
            {
                TtsStorageRoot = "C:"
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.Equal(@"C:\", result.StorageRoot);
        Assert.Equal(@"C:\models", result.ModelsRoot);
        Assert.Equal(@"C:\cache", result.CacheRoot);
        Assert.Equal(@"C:\temp", result.TempRoot);
        Assert.Equal(@"C:\logs", result.LogsRoot);
        Assert.Equal(@"C:\venv", result.VenvRoot);
    }

    [Fact]
    public void Resolve_WhenWindowsAndNoExplicitRoot_PicksFirstWritableNonSystemDrive()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions()),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true),
                    new ReplyTtsDriveDescriptor(@"D:\", isReady: true, isWritable: true),
                    new ReplyTtsDriveDescriptor(@"E:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.Equal(@"D:\webcode\melotts", result.StorageRoot);
    }

    [Fact]
    public void Resolve_WhenWindowsOnlyHasSystemDriveAndSystemDriveInstallIsDisallowed_ReturnsUnavailable()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions
            {
                AllowSystemDriveInstall = false
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.False(result.IsAvailable);
        Assert.Null(result.StorageRoot);
        Assert.Contains("C:\\", result.Message, StringComparison.Ordinal);
        Assert.Contains("AllowSystemDriveInstall", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenWindowsOnlyHasSystemDriveAndSystemDriveInstallIsAllowed_UsesSystemDrive()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions
            {
                AllowSystemDriveInstall = true
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.Equal(@"C:\webcode\melotts", result.StorageRoot);
    }

    [Fact]
    public void Resolve_WhenNonWindowsAndNoExplicitRoot_UsesDefaultDataPath()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions()),
            new FakeReplyTtsHostEnvironment(
                isWindows: false,
                systemDriveRoot: null,
                drives: []));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.Equal("/data/webcode/melotts", result.StorageRoot);
    }

    [Fact]
    public void Resolve_WhenStorageRootIsResolved_HelperSubpathsStayUnderStorageRoot()
    {
        var resolver = new ReplyTtsStorageRootResolver(
            CreateMonitor(new FeishuReplyTtsOptions
            {
                TtsStorageRoot = @"D:\tts-root"
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"D:\", isReady: true, isWritable: true)
                ]));

        var result = resolver.Resolve();

        Assert.True(result.IsAvailable);
        Assert.All(
            new[]
            {
                result.ModelsRoot,
                result.CacheRoot,
                result.TempRoot,
                result.LogsRoot,
                result.VenvRoot
            },
            path => Assert.StartsWith(result.StorageRoot!, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_WhenOptionsChangeAfterConstruction_UsesUpdatedValues()
    {
        var optionsMonitor = CreateMonitor(new FeishuReplyTtsOptions());
        var resolver = new ReplyTtsStorageRootResolver(
            optionsMonitor,
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true),
                    new ReplyTtsDriveDescriptor(@"D:\", isReady: true, isWritable: true)
                ]));

        var initialResult = resolver.Resolve();
        optionsMonitor.Set(new FeishuReplyTtsOptions
        {
            TtsStorageRoot = @"E:\override-root"
        });
        var updatedResult = resolver.Resolve();

        Assert.Equal(@"D:\webcode\melotts", initialResult.StorageRoot);
        Assert.Equal(@"E:\override-root", updatedResult.StorageRoot);
    }

    [Fact]
    public void ProbeTargetDirectory_UsesDisposableSandboxInsteadOfRealInstallTree()
    {
        var systemEnvironmentType = typeof(ReplyTtsStorageRootResolver)
            .GetNestedType("SystemReplyTtsHostEnvironment", BindingFlags.NonPublic);

        Assert.NotNull(systemEnvironmentType);

        var method = systemEnvironmentType!
            .GetMethod("BuildProbeTargetDirectory", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var probeTargetDirectory = (string)method!.Invoke(null, [@"D:\", "probe-token"])!;

        Assert.Equal(
            @"D:\.webcode-feishu-reply-tts-probe-probe-token\webcode\melotts",
            probeTargetDirectory);
        Assert.False(
            probeTargetDirectory.StartsWith(@"D:\webcode\melotts", StringComparison.OrdinalIgnoreCase));
    }

    private static MutableOptionsMonitor<FeishuReplyTtsOptions> CreateMonitor(FeishuReplyTtsOptions options)
    {
        return new MutableOptionsMonitor<FeishuReplyTtsOptions>(options);
    }

    private sealed class FakeReplyTtsHostEnvironment : IReplyTtsHostEnvironment
    {
        private readonly IReadOnlyList<ReplyTtsDriveDescriptor> _drives;

        public FakeReplyTtsHostEnvironment(
            bool isWindows,
            string? systemDriveRoot,
            IReadOnlyList<ReplyTtsDriveDescriptor> drives)
        {
            IsWindows = isWindows;
            SystemDriveRoot = systemDriveRoot;
            _drives = drives;
        }

        public bool IsWindows { get; }

        public string? SystemDriveRoot { get; }

        public IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives()
        {
            return _drives;
        }
    }

    private sealed class MutableOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        private TOptions _currentValue;

        public MutableOptionsMonitor(TOptions currentValue)
        {
            _currentValue = currentValue;
        }

        public TOptions CurrentValue => _currentValue;

        public TOptions Get(string? name) => _currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener)
        {
            return null;
        }

        public void Set(TOptions options)
        {
            _currentValue = options;
        }
    }
}
