using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ActivityLogTests
{
    [Fact]
    public void EntriesHaveTimestampsAndPreserveErrorDetails()
    {
        var log = new ActivityLog();
        log.Add("Upload failed\nGraph returned 400");
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] Upload failed", log.Text);
        Assert.Contains("Graph returned 400", log.Text);
    }

    [Fact]
    public void OldestEntriesAreRemovedAtCapacity()
    {
        var log = new ActivityLog(2);
        log.Add("First entry");
        log.Add("Second entry");
        log.Add("Third entry");
        Assert.DoesNotContain("First entry", log.Text);
        Assert.Contains("Second entry", log.Text);
        Assert.Contains("Third entry", log.Text);
    }

    [Fact]
    public void ClearAndBlankMessagesDoNotLeaveEntries()
    {
        var log = new ActivityLog();
        log.Add(" ");
        Assert.Empty(log.Text);
        log.Add("Example");
        log.Clear();
        Assert.Empty(log.Text);
    }
}
