namespace IntuneEpmRuleCreator.Core.Services;

/// <summary>Bounded, in-memory log. No automatic disk writes.</summary>
public sealed class ActivityLog(int capacity = 200)
{
    private readonly Queue<string> _entries = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public string Text => string.Join(Environment.NewLine + Environment.NewLine, _entries);

    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _entries.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message.Trim()}");
        while (_entries.Count > _capacity) _entries.Dequeue();
    }

    public void Clear() => _entries.Clear();
}
