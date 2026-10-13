using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class AlertTracker
{
    private readonly HashSet<string> _active = new();
    private readonly Dictionary<string, AlertDto> _details = new();
    private readonly object _gate = new();
    private long _duplicatesSuppressed;

    public static string KeyFor(string type, string mac) => $"{type}:{mac}";

    public long DuplicatesSuppressed { get { lock (_gate) return _duplicatesSuppressed; } }
    public int Count { get { lock (_gate) return _active.Count; } }

    public bool TryRaise(AlertDto alert)
    {
        lock (_gate)
        {
            if (!_active.Add(alert.Key)) { _duplicatesSuppressed++; return false; }
            _details[alert.Key] = alert;
            return true;
        }
    }

    public bool Clear(string key)
    {
        lock (_gate)
        {
            if (!_active.Remove(key)) return false;
            _details.Remove(key);
            return true;
        }
    }

    public List<AlertDto> Snapshot()
    {
        lock (_gate) return _details.Values.OrderByDescending(a => a.RaisedUtc).ToList();
    }
}