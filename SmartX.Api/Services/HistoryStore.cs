using System.Diagnostics;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class HistoryStore
{
    private readonly Dictionary<string, AvlTree<DateTime, double>> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Add(string mac, DateTime timestampUtc, double value)
    {
        lock (_gate)
        {
            if (!_trees.TryGetValue(mac, out var tree))
            {
                tree = new AvlTree<DateTime, double>();
                _trees[mac] = tree;
            }
            tree.Insert(timestampUtc, value);
        }
    }

    /// <summary>All readings between two instants (for example, 14:00 to 16:00).</summary>
    public HistoryQueryResultDto Query(string mac, DateTime fromUtc, DateTime toUtc, int limit)
    {
        var result = new HistoryQueryResultDto { MacAddress = mac };
        lock (_gate)
        {
            if (!_trees.TryGetValue(mac, out var tree)) return result;

            var sw = Stopwatch.StartNew();
            var hits = tree.Range(fromUtc, toUtc, limit + 1);   
            sw.Stop();

            result.TotalStored = tree.Count;
            result.TreeHeight = tree.Height;
            result.QueryMicroseconds = Math.Round(sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency, 1);
            result.Truncated = hits.Count > limit;
            foreach (var kv in hits.Take(limit))
                result.Points.Add(new TimelinePointDto { TimestampUtc = kv.Key, Value = kv.Value });
            result.Returned = result.Points.Count;
        }
        return result;
    }

    /// <summary>The newest N readingst.</summary>
    public HistoryQueryResultDto Latest(string mac, int count)
    {
        var result = new HistoryQueryResultDto { MacAddress = mac };
        lock (_gate)
        {
            if (!_trees.TryGetValue(mac, out var tree)) return result;

            var sw = Stopwatch.StartNew();
            var hits = tree.Latest(count);
            sw.Stop();

            result.TotalStored = tree.Count;
            result.TreeHeight = tree.Height;
            result.QueryMicroseconds = Math.Round(sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency, 1);
            foreach (var kv in hits)
                result.Points.Add(new TimelinePointDto { TimestampUtc = kv.Key, Value = kv.Value });
            result.Returned = result.Points.Count;
        }
        return result;
    }
}