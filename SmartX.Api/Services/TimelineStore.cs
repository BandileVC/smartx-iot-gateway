using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class TimelineStore
{
    private const int MaxPointsPerDevice = 2000;
    private readonly Dictionary<string, SortedDictionary<DateTime, double>> _timelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Add(string mac, DateTime ts, double value)
    {
        lock (_gate)
        {
            if (!_timelines.TryGetValue(mac, out var tl)) _timelines[mac] = tl = new();
            while (tl.ContainsKey(ts)) ts = ts.AddTicks(1);        
            tl[ts] = value;
            if (tl.Count > MaxPointsPerDevice) tl.Remove(tl.Keys.First());   
        }
    }

    public List<TimelinePointDto> Latest(string mac, int take)
    {
        lock (_gate)
        {
            if (!_timelines.TryGetValue(mac, out var tl)) return new();
            return tl.Skip(Math.Max(0, tl.Count - take))
                     .Select(kv => new TimelinePointDto { TimestampUtc = kv.Key, Value = kv.Value }).ToList();
        }
    }

    public List<(DateTime At, double Value)> Recent(string mac, int take)
    {
        lock (_gate)
        {
            if (!_timelines.TryGetValue(mac, out var tl)) return new();
            return tl.Skip(Math.Max(0, tl.Count - take)).Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }
}