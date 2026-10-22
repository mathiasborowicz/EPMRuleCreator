using System.Text.RegularExpressions;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class DocumentationTests
{
    [Fact]
    public void PublishedMarkdownHasNoBrokenLocalLinksOrImages()
    {
        foreach (var file in Pages())
        {
            var markdown = Regex.Replace(File.ReadAllText(file), @"```[\s\S]*?```", string.Empty);
            markdown = Regex.Replace(markdown, @"<!--[\s\S]*?-->", string.Empty);
            foreach (Match match in Regex.Matches(markdown, @"\]\(([^\s)]+)\)"))
            {
                var target = match.Groups[1].Value;
                if (target.StartsWith('#') || Uri.TryCreate(target, UriKind.Absolute, out _)) continue;
                var path = Uri.UnescapeDataString(target.Split('#')[0]);
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!, path)), $"Broken link in {file}: {target}");
            }
        }
    }

    [Fact]
    public void EveryScreenshotSlotHasANamedFileInTheChecklist()
    {
        var checklist = File.ReadAllText(Path.Combine(Root(), "docs", "Screenshots.md"));
        var slots = Pages().SelectMany(file => Regex.Matches(File.ReadAllText(file), @"<!-- SCREENSHOT: ([\w-]+)")
            .Select(match => match.Groups[1].Value)).Distinct().ToArray();
        Assert.Equal(6, slots.Length);
        foreach (var slot in slots) Assert.Contains($"`{slot}.png`", checklist);
    }

    [Fact]
    public void ReadmeKeepsSetupActionableWithoutCacheInternals()
    {
        var readme = File.ReadAllText(Path.Combine(Root(), "README.md"));
        Assert.DoesNotContain("msal.cache", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DPAPI", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tokens stay in RAM", readme, StringComparison.OrdinalIgnoreCase);
        foreach (var step in new[] { "New registration", "Mobile and desktop applications", "http://localhost", "Delegated permissions", "User.Read", "DeviceManagementConfiguration.ReadWrite.All", "Grant admin consent", "Directory (tenant) ID", "settings.json" })
            Assert.Contains(step, readme);
    }

    private static IEnumerable<string> Pages() => Directory.GetFiles(Path.Combine(Root(), "docs"), "*.md")
        .Append(Path.Combine(Root(), "README.md"));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EPMRuleCreator.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
