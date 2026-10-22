using IntuneEpmRuleCreator.Core.Services;
using Xunit;

namespace IntuneEpmRuleCreator.Tests;

public sealed class ExplorerApplicationDeploymentTests
{
    [Fact]
    public void InstalledCopy_SurvivesMovingAndDeletingTheDownloadedExe()
    {
        using var files = new DeploymentFiles();
        var source = files.Download("download.exe", "original application");
        var installed = ExplorerApplicationDeployment.Install(source, files.AppData);

        var moved = Path.Combine(files.Root, "moved.exe");
        File.Move(source, moved);
        File.Delete(moved);

        Assert.Equal(Path.Combine(files.AppData, "IntuneEpmRuleCreator", "App", "IntuneEpmRuleCreator.exe"), installed);
        Assert.Equal("original application", File.ReadAllText(installed));
        Assert.DoesNotContain("download.exe", ExplorerContextMenuService.BuildCommand(installed));
        Assert.Equal('"' + installed + "\" \"%1\"", ExplorerContextMenuService.BuildCommand(installed));
    }

    [Fact]
    public void UpdatingFromANewDownload_ReplacesTheCopyAtTheSameStablePath()
    {
        using var files = new DeploymentFiles();
        var oldSource = files.Download("old.exe", "old release");
        var installed = ExplorerApplicationDeployment.Install(oldSource, files.AppData);
        var newSource = files.Download("new.exe", "new release");

        Assert.Equal(installed, ExplorerApplicationDeployment.Install(newSource, files.AppData));
        Assert.Equal("new release", File.ReadAllText(installed));
        Assert.Equal("old release", File.ReadAllText(oldSource));
        Assert.Equal("new release", File.ReadAllText(newSource));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.tmp"));
    }

    [Fact]
    public void RegisteringTheManagedCopy_DoesNotCopyTheFileOntoItself()
    {
        using var files = new DeploymentFiles();
        var installed = ExplorerApplicationDeployment.Install(files.Download("app.exe", "release"), files.AppData);
        var timestamp = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(installed, timestamp);
        timestamp = File.GetLastWriteTimeUtc(installed);

        Assert.Equal(installed, ExplorerApplicationDeployment.Install(installed, files.AppData));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(installed));
    }

    [Fact]
    public void IdenticalDownloads_DoNotNeedToReplaceTheManagedFile()
    {
        using var files = new DeploymentFiles();
        var source = files.Download("app.exe", "same release");
        var installed = ExplorerApplicationDeployment.Install(source, files.AppData);
        File.SetLastWriteTimeUtc(installed, DateTime.UtcNow.AddDays(-2));
        var timestamp = File.GetLastWriteTimeUtc(installed);

        ExplorerApplicationDeployment.Install(source, files.AppData);

        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(installed));
    }

    [Fact]
    public void InvalidDownload_PreservesThePreviouslyInstalledCopy()
    {
        using var files = new DeploymentFiles();
        var installed = ExplorerApplicationDeployment.Install(files.Download("app.exe", "working release"), files.AppData);

        Assert.Throws<InvalidOperationException>(() => ExplorerApplicationDeployment.Install(Path.Combine(files.Root, "missing.exe"), files.AppData));
        Assert.Throws<InvalidOperationException>(() => ExplorerApplicationDeployment.Install(files.Download("dotnet.exe", "host"), files.AppData));
        Assert.Throws<InvalidOperationException>(() => ExplorerApplicationDeployment.Install(files.Download("script.ps1", "script"), files.AppData));
        Assert.Equal("working release", File.ReadAllText(installed));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.tmp"));
    }

    [Fact]
    public void FailedReplacement_CleansItsStagingFile()
    {
        using var files = new DeploymentFiles();
        var installed = ExplorerApplicationDeployment.GetInstalledPath(files.AppData);
        Directory.CreateDirectory(installed); // An existing directory cannot be replaced by an EXE.

        var exception = Record.Exception(() => ExplorerApplicationDeployment.Install(files.Download("app.exe", "release"), files.AppData));
        Assert.True(exception is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(installed));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.tmp"));
    }

    private sealed class DeploymentFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "EpmRuleCreator-deployment-test-" + Guid.NewGuid().ToString("N"));
        public string AppData => Path.Combine(Root, "Local AppData");
        public DeploymentFiles() => Directory.CreateDirectory(Root);
        public string Download(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
