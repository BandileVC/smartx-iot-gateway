namespace SmartX.Shared.Collections;

/// <summary>One directed half of an undirected wireless link: the neighbour and its power cost.</summary>
public record MeshLink(string To, double Cost);

public record MstEdge(string From, string To, double Cost);

public class MstResult
{
    public string Root { get; set; } = "";
    public List<MstEdge> Edges { get; } = new();
    public Dictionary<string, string> Parent { get; } = new(StringComparer.OrdinalIgnoreCase);   // child -> next hop
    public double TotalCost { get; set; }
}

public class BfsResult
{
    public List<string> Order { get; } = new();
    public Dictionary<string, int> Hops { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Parent { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Undirected weighted graph of the IoT mesh. Vertices are ESP32 nodes (plus the gateway); edge
/// weights are the relative transmit power of a link. Nodes that are down are tracked in a set and
/// skipped by every traversal, so one structure models both the physical mesh and its live health.
/// </summary>
public class MeshGraph
{
    private readonly Dictionary<string, List<MeshLink>> _adjacency = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);

    public int VertexCount => _adjacency.Count;
    public IEnumerable<string> Vertices => _adjacency.Keys;

    public bool HasVertex(string id) => _adjacency.ContainsKey(id);

    public void AddVertex(string id)
    {
        if (!_adjacency.ContainsKey(id)) _adjacency[id] = new List<MeshLink>();
    }

    public void AddEdge(string a, string b, double cost)
    {
        AddVertex(a);
        AddVertex(b);
        if (HasEdge(a, b)) return;
        _adjacency[a].Add(new MeshLink(b, cost));
        _adjacency[b].Add(new MeshLink(a, cost));
    }

    public bool HasEdge(string a, string b)
    {
        if (!_adjacency.TryGetValue(a, out var list)) return false;
        foreach (var link in list)
        {
            if (string.Equals(link.To, b, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public IReadOnlyList<MeshLink> Neighbours(string id) =>
        _adjacency.TryGetValue(id, out var list) ? list : new List<MeshLink>();

    
    public void SetDown(string id, bool down)
    {
        if (down) _down.Add(id);
        else _down.Remove(id);
    }

    public bool IsDown(string id) => _down.Contains(id);
    public void ClearDown() => _down.Clear();

    
    public double? LinkCost(string a, string b)
    {
        if (!_adjacency.TryGetValue(a, out var list)) return null;
        foreach (var link in list)
        {
            if (string.Equals(link.To, b, StringComparison.OrdinalIgnoreCase)) return link.Cost;
        }
        return null;
    }

    public double PathCost(IReadOnlyList<string> path)
    {
        double total = 0;
        for (var i = 0; i + 1 < path.Count; i++)
            total += LinkCost(path[i], path[i + 1]) ?? 0;
        return total;
    }

    /// <summary>Every undirected edge exactly once.</summary>
    public List<(string A, string B, double Cost)> Edges()
    {
        var list = new List<(string A, string B, double Cost)>();
        foreach (var pair in _adjacency)
        {
            foreach (var link in pair.Value)
            {
                if (string.Compare(pair.Key, link.To, StringComparison.OrdinalIgnoreCase) < 0)
                    list.Add((pair.Key, link.To, link.Cost));
            }
        }
        return list;
    }

        /// <summary>
    /// Breadth-first search from 'start'. Visits nodes level by level using a FIFO Queue, so the first time
    /// a node is reached is via the fewest possible hops. Down nodes are never entered when respectDown is true.
    /// O(V + E).
    /// </summary>
    public BfsResult Bfs(string start, bool respectDown = true)
    {
        var result = new BfsResult();
        if (!_adjacency.ContainsKey(start)) return result;
        if (respectDown && IsDown(start)) return result;

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        queue.Enqueue(start);
        result.Hops[start] = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Order.Add(current);

            foreach (var link in _adjacency[current])
            {
                if (visited.Contains(link.To)) continue;
                if (respectDown && IsDown(link.To)) continue;

                visited.Add(link.To);
                result.Parent[link.To] = current;
                result.Hops[link.To] = result.Hops[current] + 1;
                queue.Enqueue(link.To);
            }
        }
        return result;
    }

    /// <summary>Walks parent pointers from a node up to the root. Returns node -> ... -> root, or empty if there is no route.</summary>
    public static List<string> PathToRoot(IReadOnlyDictionary<string, string> parent, string root, string node)
    {
        var path = new List<string> { node };
        var current = node;
        var guard = 0;

        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (!parent.TryGetValue(current, out var next) || ++guard > 10000)
                return new List<string>();
            path.Add(next);
            current = next;
        }
        return path;
    }

        /// <summary>
    /// Prim's algorithm, grown from the gateway. A binary min-heap (PriorityQueue) always yields the cheapest
    /// link leaving the current tree; if it reaches an already-connected node it would form a cycle, so it is
    /// skipped. Result: a spanning tree touching every live node with minimum total transmit power.
    /// O(E log E).
    /// </summary>
    public MstResult Prim(string root, bool respectDown = true)
    {
        var result = new MstResult { Root = root };
        if (!_adjacency.ContainsKey(root)) return result;
        if (respectDown && IsDown(root)) return result;

        var inTree = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var heap = new PriorityQueue<MstEdge, double>();

        foreach (var link in _adjacency[root])
        {
            if (respectDown && IsDown(link.To)) continue;
            heap.Enqueue(new MstEdge(root, link.To, link.Cost), link.Cost);
        }

        while (heap.TryDequeue(out var edge, out _))
        {
            if (inTree.Contains(edge.To)) continue;      

            inTree.Add(edge.To);
            result.Edges.Add(edge);
            result.Parent[edge.To] = edge.From;          
            result.TotalCost += edge.Cost;

            foreach (var link in _adjacency[edge.To])
            {
                if (inTree.Contains(link.To)) continue;
                if (respectDown && IsDown(link.To)) continue;
                heap.Enqueue(new MstEdge(edge.To, link.To, link.Cost), link.Cost);
            }
        }
        return result;
    }
}