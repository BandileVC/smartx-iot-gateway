using System.Collections;
using System.Diagnostics;
using SmartX.Shared.Models;

namespace SmartX.Shared.Collections;

/// <summary>
/// Custom self-built data structure (NOT a wrapper around List/Dictionary) used to
/// store a sensor's historical telemetry readings, keyed by timestamp (ticks).
///
/// A binary search tree gives O(log n) average insertion and O(log n) average
/// range-search performance, which matters once a sensor has accumulated tens of
/// thousands of readings — far better than a linear scan over a List&lt;T&gt; for
/// "find all readings between time X and Y" queries used by the historical search
/// feature and the performance-test screen.
///
/// Implements IEnumerable so it behaves like a first-class .NET collection
/// (foreach, LINQ, etc.) while the internal storage remains a custom tree.
/// </summary>
public class TelemetryHistory<T> : IEnumerable<TelemetryPacket<T>> where T : struct
{
    private class Node
    {
        public required TelemetryPacket<T> Packet;
        public Node? Left;
        public Node? Right;
    }

    private Node? _root;
    private int _count;

    public int Count => _count;

    /// <summary>Ticks elapsed for the most recent Insert call (for the performance dashboard).</summary>
    public long LastInsertTicks { get; private set; }

    /// <summary>Ticks elapsed for the most recent SearchRange call (for the performance dashboard).</summary>
    public long LastSearchTicks { get; private set; }

    public void Insert(TelemetryPacket<T> packet)
    {
        var sw = Stopwatch.StartNew();
        _root = InsertRecursive(_root, packet);
        _count++;
        sw.Stop();
        LastInsertTicks = sw.ElapsedTicks;
    }

    // Recursion is used here for the tree insertion itself — a second, distinct
    // application of recursion from the deployment-hierarchy validator.
    private static Node InsertRecursive(Node? node, TelemetryPacket<T> packet)
    {
        if (node is null)
            return new Node { Packet = packet };

        if (packet.TimestampUtc.Ticks < node.Packet.TimestampUtc.Ticks)
            node.Left = InsertRecursive(node.Left, packet);
        else
            node.Right = InsertRecursive(node.Right, packet);

        return node;
    }

    /// <summary>Returns all readings with TimestampUtc within [fromUtc, toUtc], inclusive.</summary>
    public List<TelemetryPacket<T>> SearchRange(DateTime fromUtc, DateTime toUtc)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<TelemetryPacket<T>>();
        SearchRangeRecursive(_root, fromUtc.Ticks, toUtc.Ticks, results);
        sw.Stop();
        LastSearchTicks = sw.ElapsedTicks;
        return results;
    }

    private static void SearchRangeRecursive(Node? node, long fromTicks, long toTicks, List<TelemetryPacket<T>> results)
    {
        if (node is null) return;

        // Prune left/right subtrees that cannot possibly contain matches.
        if (node.Packet.TimestampUtc.Ticks > fromTicks)
            SearchRangeRecursive(node.Left, fromTicks, toTicks, results);

        if (node.Packet.TimestampUtc.Ticks >= fromTicks && node.Packet.TimestampUtc.Ticks <= toTicks)
            results.Add(node.Packet);

        if (node.Packet.TimestampUtc.Ticks < toTicks)
            SearchRangeRecursive(node.Right, fromTicks, toTicks, results);
    }

    /// <summary>Returns the single most recent reading (right-most node), O(log n).</summary>
    public TelemetryPacket<T>? GetLatest()
    {
        var node = _root;
        if (node is null) return null;
        while (node.Right is not null) node = node.Right;
        return node.Packet;
    }

    // In-order traversal -> yields readings sorted by timestamp ascending.
    public IEnumerator<TelemetryPacket<T>> GetEnumerator()
    {
        foreach (var packet in InOrder(_root))
            yield return packet;
    }

    private static IEnumerable<TelemetryPacket<T>> InOrder(Node? node)
    {
        if (node is null) yield break;
        foreach (var p in InOrder(node.Left)) yield return p;
        yield return node.Packet;
        foreach (var p in InOrder(node.Right)) yield return p;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
