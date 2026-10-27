using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ApplicationModeTests
{
    [Fact]
    public void StandaloneExecutableIsPortableWithoutWritingInstallationState()
    {
        using var files = new Files();
        var executable = files.Create("download.exe", "portable");
        Assert.Equal(ApplicationMode.Portable, ApplicationModeService.Resolve(executable, files.AppData));
        Assert.Equal("portable", File.ReadAllText(executable));
        Assert.False(Directory.Exists(files.AppData));
    }

    [Theory]
    [InlineData("{ABCDEFAB-1234-1234-1234-ABCDEFABCDEF}", "abcdefab-1234-1234-1234-abcdefabcdef")]
    [InlineData("ABCDEFAB-1234-1234-1234-ABCDEFABCDEF", "{abcdefab-1234-1234-1234-abcdefabcdef}")]
    public void MsiRegistrationMatchesExecutableAndUninstallProductCode(string productCode, string uninstallProductCode)
    {
        using var files = new Files();
        var executable = files.Create("installed.exe", "msi");

        var installLocation = Path.GetDirectoryName(executable)!;
        Assert.True(ApplicationModeService.MatchesMsiInstallation(executable, executable, productCode, uninstallProductCode));
        Assert.True(ApplicationModeService.MatchesMsiInstallation(
            executable, executable, productCode, uninstallProductCode,
            "1.0.0", "1.0.0", "1.0.0", installLocation, installLocation));
        Assert.False(ApplicationModeService.MatchesMsiInstallation(
            executable, files.Create("other.exe", "msi"), productCode, uninstallProductCode,
            "1.0.0", "1.0.0", "1.0.0", installLocation, installLocation));
        Assert.False(ApplicationModeService.MatchesMsiInstallation(
            executable, executable, productCode, "{00000000-0000-0000-0000-000000000000}",
            "1.0.0", "1.0.0", "1.0.0", installLocation, installLocation));
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
