using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ApplicationModeTests
{
    [Fact]
    public void PortableChoiceRemembersFolderWithoutInstallingAnExe()
    {
        using var files = new Files();
        var executable = files.Create("download.exe", "portable");
        Assert.Equal(ApplicationMode.Choose, ApplicationModeService.Resolve(executable, files.AppData));
        ApplicationModeService.RememberPortable(executable);
        ApplicationModeService.RememberPortable(executable);
        Assert.Equal(ApplicationMode.Portable, ApplicationModeService.Resolve(executable, files.AppData));
        Assert.Equal("portable", File.ReadAllText(executable));
        Assert.False(Directory.Exists(files.AppData));
        Assert.Equal(0, new FileInfo(ApplicationModeService.PortableMarker(executable)).Length);
    }

    [Fact]
    public void InstalledLocationIsRecognizedWithoutWritingAnySettingsOrMarker()
    {
        using var files = new Files();
        var installed = ExplorerApplicationDeployment.GetInstalledPath(files.AppData);
        Assert.Equal(ApplicationMode.Installed, ApplicationModeService.Resolve(installed, files.AppData));
        Assert.False(Directory.Exists(files.AppData));
    }

    [Fact]
    public void ExistingInstallationDoesNotOverridePortableModeOrGetOverwritten()
    {
        using var files = new Files();
        var original = files.Create("original.exe", "installed release");
        var installed = ExplorerApplicationDeployment.Install(original, files.AppData);
        var portable = files.Create("portable.exe", "different release");
        Assert.Equal(ApplicationMode.Choose, ApplicationModeService.Resolve(portable, files.AppData));
        ApplicationModeService.RememberPortable(portable);
        Assert.Equal(ApplicationMode.Portable, ApplicationModeService.Resolve(portable, files.AppData));
        Assert.Equal(installed, ExplorerApplicationDeployment.Install(portable, files.AppData, overwriteExisting: false));
        Assert.Equal("installed release", File.ReadAllText(installed));
        Assert.Equal("different release", File.ReadAllText(portable));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.tmp"));
    }

    [Fact]
    public void MenuUsesExactSelectedExecutableWithoutDeployingACopy()
    {
        using var files = new Files();
        var portable = files.Create("portable.exe", "portable");
        Assert.Equal(portable, ExplorerContextMenuService.ResolveExecutablePath(portable));
        Assert.False(Directory.Exists(files.AppData));
        Assert.Throws<InvalidOperationException>(() => ExplorerContextMenuService.ResolveExecutablePath(files.Create("dotnet.exe", "host")));
        Assert.Throws<InvalidOperationException>(() => ExplorerContextMenuService.ResolveExecutablePath(files.Create("not-exe.txt", "text")));
    }

    private sealed class Files : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("epm-modes-test-");
        public string AppData => Path.Combine(_root.FullName, "AppData");
        public string Create(string name, string text)
        {
            var path = Path.Combine(_root.FullName, name);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() => _root.Delete(recursive: true);
    }
}
