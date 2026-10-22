using IntuneEpmRuleCreator.Core.Services;
using Xunit;

namespace IntuneEpmRuleCreator.Tests;

public sealed class ExplorerContextMenuTests
{
    [Theory]
    [InlineData(@"C:\Git\EPMRuleCreator\IntuneEpmRuleCreator.exe")]
    [InlineData(@"C:\My Apps\Intune EPM\IntuneEpmRuleCreator.exe")]
    [InlineData(@"C:\Benutzer\Jörg\工具\IntuneEpmRuleCreator.exe")]
    public void ExplorerCommand_QuotesTheAppAndSelectedFile(string executable)
    {
        // Explorer substitutes %1 with the selected file; it must remain a single
        // quoted argument, and launching the menu must not execute that file.
        Assert.Equal('"' + executable + "\" \"%1\"", ExplorerContextMenuService.BuildCommand(executable));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\App\"\\tool.exe")]
    [InlineData("C:\\App\n\\tool.exe")]
    [InlineData("C:\\App\r\\tool.exe")]
    [InlineData("C:\\App\0\\tool.exe")]
    public void InvalidExecutablePaths_CannotChangeTheExplorerCommand(string executable)
    {
        Assert.ThrowsAny<ArgumentException>(() => ExplorerContextMenuService.BuildCommand(executable));
    }
}
