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

    // BFS (Step 4) and Prim (Step 5) go below this line, inside the class.
}