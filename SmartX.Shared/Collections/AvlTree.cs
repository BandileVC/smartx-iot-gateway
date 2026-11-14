using SmartX.Shared.Models;

namespace SmartX.Shared.Collections;

/// <summary>
/// Self-balancing AVL tree. After every insert, each node's left and right subtree heights
/// differ by at most 1, so the tree height stays at roughly 1.44 * log2(n).
/// Inserts and range queries are therefore O(log n) even when keys arrive in sorted order
/// (which is exactly how timestamped telemetry arrives).
/// Duplicate keys are allowed (two readings can share a timestamp tick).
/// </summary>
public class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public Node(TKey key, TValue value)
        {
            Key = key;
            Value = value;
            Height = 1;
        }

        public TKey Key;
        public TValue Value;
        public Node? Left;
        public Node? Right;
        public int Height;
    }

    private Node? _root;

    public int Count { get; private set; }

    /// <summary>How many rotations were needed to keep the tree balanced (shown in benchmarks).</summary>
    public long RotationCount { get; private set; }

    public int Height => HeightOf(_root);

        public void Insert(TKey key, TValue value)
    {
        _root = Insert(_root, key, value);
        Count++;
    }

    private Node Insert(Node? node, TKey key, TValue value)
    {
        if (node is null) return new Node(key, value);

        if (key.CompareTo(node.Key) < 0)
            node.Left = Insert(node.Left, key, value);
        else
            node.Right = Insert(node.Right, key, value);   // equal keys go right

        return Rebalance(node);
    }

    private static int HeightOf(Node? node) => node is null ? 0 : node.Height;

    private static void UpdateHeight(Node node)
    {
        node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));
    }

    private static int BalanceFactor(Node node) => HeightOf(node.Left) - HeightOf(node.Right);

    private Node Rebalance(Node node)
    {
        UpdateHeight(node);
        var balance = BalanceFactor(node);

        if (balance > 1)                                   // left-heavy
        {
            if (BalanceFactor(node.Left!) < 0)             // left-right case
                node.Left = RotateLeft(node.Left!);
            return RotateRight(node);                      // left-left case
        }

        if (balance < -1)                                  // right-heavy
        {
            if (BalanceFactor(node.Right!) > 0)            // right-left case
                node.Right = RotateRight(node.Right!);
            return RotateLeft(node);                       // right-right case
        }

        return node;
    }

    private Node RotateRight(Node y)
    {
        var x = y.Left!;
        y.Left = x.Right;
        x.Right = y;
        UpdateHeight(y);
        UpdateHeight(x);
        RotationCount++;
        return x;
    }

    private Node RotateLeft(Node x)
    {
        var y = x.Right!;
        x.Right = y.Left;
        y.Left = x;
        UpdateHeight(x);
        UpdateHeight(y);
        RotationCount++;
        return y;
    }

        /// <summary>
    /// All entries with from &lt;= key &lt;= to, in ascending key order.
    /// Whole subtrees outside the range are skipped, so the cost is O(log n + k)
    /// where k is the number of matches. An optional limit stops the walk early.
    /// </summary>
    public List<KeyValuePair<TKey, TValue>> Range(TKey from, TKey to, int limit = int.MaxValue)
    {
        var results = new List<KeyValuePair<TKey, TValue>>();
        RangeWalk(_root, from, to, limit, results);
        return results;
    }

    private static void RangeWalk(Node? node, TKey from, TKey to, int limit,
        List<KeyValuePair<TKey, TValue>> results)
    {
        if (node is null || results.Count >= limit) return;

        var aboveFrom = from.CompareTo(node.Key) <= 0;     // node.Key >= from
        var belowTo = node.Key.CompareTo(to) <= 0;         // node.Key <= to

        if (aboveFrom)
            RangeWalk(node.Left, from, to, limit, results);   // left subtree may still hold matches

        if (aboveFrom && belowTo && results.Count < limit)
            results.Add(new KeyValuePair<TKey, TValue>(node.Key, node.Value));

        if (belowTo)
            RangeWalk(node.Right, from, to, limit, results);  // right subtree may still hold matches
    }

    /// <summary>The newest 'count' entries (largest keys first). Used for the "latest readings" log.</summary>
    public List<KeyValuePair<TKey, TValue>> Latest(int count)
    {
        var results = new List<KeyValuePair<TKey, TValue>>();
        LatestWalk(_root, count, results);
        return results;
    }

    private static void LatestWalk(Node? node, int count, List<KeyValuePair<TKey, TValue>> results)
    {
        if (node is null || results.Count >= count) return;

        LatestWalk(node.Right, count, results);            // largest keys first
        if (results.Count < count)
            results.Add(new KeyValuePair<TKey, TValue>(node.Key, node.Value));
        LatestWalk(node.Left, count, results);
    }

    /// <summary>Proof of balance: true only if every node's subtrees differ in height by at most 1.</summary>
    public bool VerifyBalanced() => CheckBalanced(_root) >= 0;

    private static int CheckBalanced(Node? node)
    {
        if (node is null) return 0;

        var left = CheckBalanced(node.Left);
        if (left < 0) return -1;

        var right = CheckBalanced(node.Right);
        if (right < 0) return -1;

        if (Math.Abs(left - right) > 1) return -1;
        return 1 + Math.Max(left, right);
    }
}