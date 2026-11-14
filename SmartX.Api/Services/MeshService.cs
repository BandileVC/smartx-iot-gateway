using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class MeshService
{
    public const string GatewayMac = "GW:00:00:00:00:01";
    private const double RadioRange = 0.31;
    private static readonly double[] RingRadii = { 0.18, 0.34, 0.44 };
    private static readonly int[] RingCapacity = { 5, 7, int.MaxValue };

    private readonly DeviceRegistry _registry;
    private readonly IHubContext<TelemetryHub> _hub;
    private readonly object _gate = new();
    private readonly HashSet<string> _manualDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double X, double Y)> _positions = new(StringComparer.OrdinalIgnoreCase);

    private MeshGraph _graph = new();
    private string _topologySignature = "";
    private string _broadcastSignature = "";

    public MeshService(DeviceRegistry registry, IHubContext<TelemetryHub> hub)
    {
        _registry = registry;
        _hub = hub;
    }

    // ========== Public API ===========

    public MeshAnalysisDto Analyze()
    {
        var devices = _registry.All();
        lock (_gate)
        {
            PrepareLocked(devices);
            return BuildAnalysisLocked(devices);
        }
    }

    public RouteDto? Route(string mac)
    {
        mac = DeviceRegistry.NormalizeMac(mac);
        var devices = _registry.All();
        lock (_gate)
        {
            PrepareLocked(devices);
            if (!_graph.HasVertex(mac)) return null;

            var names = NameMap(devices);
            var bfs = _graph.Bfs(GatewayMac);
            var mst = _graph.Prim(GatewayMac);
            var bfsPath = MeshGraph.PathToRoot(bfs.Parent, GatewayMac, mac);
            var mstPath = MeshGraph.PathToRoot(mst.Parent, GatewayMac, mac);

            return new RouteDto
            {
                MacAddress = mac,
                Name = NameOf(names, mac),
                Found = mstPath.Count > 0,
                MstPath = mstPath,
                MstRoute = Describe(mstPath, names),
                MstCost = Math.Round(_graph.PathCost(mstPath), 1),
                MstHops = Math.Max(mstPath.Count - 1, 0),
                BfsPath = bfsPath,
                BfsRoute = Describe(bfsPath, names),
                BfsCost = Math.Round(_graph.PathCost(bfsPath), 1),
                BfsHops = Math.Max(bfsPath.Count - 1, 0)
            };
        }
    }

    /// <summary>BFS from the gateway (or any node): who is connected, who is down, who is cut off.</summary>
    public ConnectedDto Connected(string? startMac)
    {
        var devices = _registry.All();
        lock (_gate)
        {
            PrepareLocked(devices);
            var names = NameMap(devices);
            var start = string.IsNullOrWhiteSpace(startMac) ? GatewayMac : DeviceRegistry.NormalizeMac(startMac);
            var bfs = _graph.Bfs(start);

            var dto = new ConnectedDto { StartMac = start };
            foreach (var v in bfs.Order)
                dto.Connected.Add(new ConnectedNodeDto { MacAddress = v, Name = NameOf(names, v), Hops = bfs.Hops[v] });

            foreach (var v in _graph.Vertices)
            {
                if (_graph.IsDown(v))
                    dto.Down.Add(new ConnectedNodeDto { MacAddress = v, Name = NameOf(names, v), Hops = -1 });
                else if (!bfs.Hops.ContainsKey(v))
                    dto.CutOff.Add(new ConnectedNodeDto { MacAddress = v, Name = NameOf(names, v), Hops = -1 });
            }
            return dto;
        }
    }

    public bool SetManualDown(string mac, bool down)
    {
        mac = DeviceRegistry.NormalizeMac(mac);
        lock (_gate)
        {
            if (string.Equals(mac, GatewayMac, StringComparison.OrdinalIgnoreCase)) return false;
            if (down) _manualDown.Add(mac);
            else _manualDown.Remove(mac);
            return true;
        }
    }

    public void RestoreAll()
    {
        lock (_gate) { _manualDown.Clear(); }
    }

    /// <summary>Pushes the fresh topology to every dashboard, but only when something actually changed.</summary>
    public async Task BroadcastIfChangedAsync(bool force, CancellationToken ct = default)
    {
        var analysis = Analyze();
        var signature = string.Join(",", analysis.Nodes.Where(n => n.IsDown).Select(n => n.MacAddress).OrderBy(m => m))
                        + "|" + analysis.Nodes.Count + "|" + analysis.CutOffCount;
        bool send;
        lock (_gate)
        {
            send = force || signature != _broadcastSignature;
            _broadcastSignature = signature;
        }
        if (send) await _hub.Clients.All.SendAsync("ReceiveTopology", analysis, ct);
    }

    // ========= Topology construction =========

    private void PrepareLocked(List<DeviceStateDto> devices)
    {
        var macs = devices.Select(d => d.MacAddress).OrderBy(m => m, StringComparer.Ordinal).ToList();
        var signature = string.Join(",", macs);
        if (signature != _topologySignature)
        {
            _topologySignature = signature;
            BuildTopologyLocked(macs);
        }

        
        _graph.ClearDown();
        foreach (var d in devices)
        {
            if (_manualDown.Contains(d.MacAddress) || d.Status == DeviceStatus.Offline)
                _graph.SetDown(d.MacAddress, true);
        }
    }

    private void BuildTopologyLocked(List<string> macs)
    {
        var graph = new MeshGraph();
        _positions.Clear();

        graph.AddVertex(GatewayMac);
        _positions[GatewayMac] = (0.5, 0.5);
        foreach (var mac in macs) graph.AddVertex(mac);

        // Deterministic ring layout around the gateway.
        var next = 0;
        for (var ring = 0; ring < RingRadii.Length && next < macs.Count; ring++)
        {
            var take = Math.Min(RingCapacity[ring], macs.Count - next);
            for (var i = 0; i < take; i++)
            {
                var angle = 2 * Math.PI * i / take + ring * 0.45 - Math.PI / 2;
                _positions[macs[next + i]] = (0.5 + RingRadii[ring] * Math.Cos(angle),
                                              0.5 + RingRadii[ring] * Math.Sin(angle));
            }
            next += take;
        }

        
        var all = new List<string> { GatewayMac };
        all.AddRange(macs);
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                var d = Distance(all[i], all[j]);
                if (d <= RadioRange) graph.AddEdge(all[i], all[j], LinkCost(d));
            }
        }

        _graph = graph;
        EnsureConnected(all);
    }

    /// <summary>If the layout stranded a node, link it to its nearest connected neighbour so the physical mesh is whole.</summary>
    private void EnsureConnected(List<string> all)
    {
        while (true)
        {
            var reach = _graph.Bfs(GatewayMac, false);
            var stranded = all.FirstOrDefault(v => !reach.Hops.ContainsKey(v));
            if (stranded is null) return;

            string? best = null;
            var bestDistance = double.MaxValue;
            foreach (var candidate in reach.Hops.Keys)
            {
                var d = Distance(stranded, candidate);
                if (d < bestDistance) { bestDistance = d; best = candidate; }
            }
            if (best is null) return;
            _graph.AddEdge(stranded, best, LinkCost(bestDistance));
        }
    }

    private double Distance(string a, string b)
    {
        var pa = _positions[a];
        var pb = _positions[b];
        var dx = pa.X - pb.X;
        var dy = pa.Y - pb.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

     
    private static double LinkCost(double distance) => Math.Round(5.0 + 400.0 * distance * distance, 1);

    // ========== Analysis ==========

    private MeshAnalysisDto BuildAnalysisLocked(List<DeviceStateDto> devices)
    {
        var byMac = new Dictionary<string, DeviceStateDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices) byMac[d.MacAddress] = d;
        var names = NameMap(devices);

        var sw = Stopwatch.StartNew();
        var bfs = _graph.Bfs(GatewayMac);
        sw.Stop();
        var bfsMicros = Math.Round(sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency, 1);

        sw.Restart();
        var mst = _graph.Prim(GatewayMac);
        sw.Stop();
        var primMicros = Math.Round(sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency, 1);

        var baseline = _graph.Prim(GatewayMac, false);   // the healthy network, ignoring failures

        
        var dependents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in mst.Parent.Keys)
        {
            var path = MeshGraph.PathToRoot(mst.Parent, GatewayMac, node);
            for (var i = 1; i < path.Count - 1; i++)
            {
                dependents.TryGetValue(path[i], out var count);
                dependents[path[i]] = count + 1;
            }
        }

        var nodes = new List<MeshNodeDto>();
        foreach (var v in _graph.Vertices)
        {
            var isGateway = string.Equals(v, GatewayMac, StringComparison.OrdinalIgnoreCase);
            byMac.TryGetValue(v, out var dev);
            dependents.TryGetValue(v, out var deps);
            bfs.Hops.TryGetValue(v, out var hops);
            if (!bfs.Hops.ContainsKey(v)) hops = -1;

            nodes.Add(new MeshNodeDto
            {
                MacAddress = v,
                Name = NameOf(names, v),
                Category = dev?.Category ?? SensorCategory.Environmental,
                IsGateway = isGateway,
                IsDown = _graph.IsDown(v),
                DownReason = !_graph.IsDown(v) ? null
                    : (_manualDown.Contains(v) ? "Simulated hardware failure" : "No telemetry (watchdog marked offline)"),
                X = _positions[v].X,
                Y = _positions[v].Y,
                Hops = hops,
                Dependents = deps
            });
        }

        var mstKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in mst.Edges) mstKeys.Add(EdgeKey(e.From, e.To));

        var edges = new List<MeshEdgeDto>();
        double fullCost = 0;
        foreach (var (left, right, cost) in _graph.Edges())
        {
            var active = !_graph.IsDown(left) && !_graph.IsDown(right);
            if (active) fullCost += cost;
            edges.Add(new MeshEdgeDto
            {
                FromMac = left, ToMac = right, Cost = cost, Active = active,
                InMst = mstKeys.Contains(EdgeKey(left, right))
            });
        }

        
        var reroutes = new List<RerouteDto>();
        foreach (var n in nodes)
        {
            if (n.IsGateway || n.IsDown) continue;

            var oldPath = MeshGraph.PathToRoot(baseline.Parent, GatewayMac, n.MacAddress);
            var newPath = MeshGraph.PathToRoot(mst.Parent, GatewayMac, n.MacAddress);
            if (oldPath.Count == 0) continue;
            if (newPath.Count > 0 && oldPath.SequenceEqual(newPath, StringComparer.OrdinalIgnoreCase)) continue;

            reroutes.Add(new RerouteDto
            {
                MacAddress = n.MacAddress,
                Name = n.Name,
                OldRoute = Describe(oldPath, names),
                NewRoute = newPath.Count == 0 ? "NO ROUTE (cut off)" : Describe(newPath, names),
                Status = newPath.Count == 0 ? "No route" : "Rerouted"
            });
        }

        return new MeshAnalysisDto
        {
            GatewayMac = GatewayMac,
            Nodes = nodes,
            Edges = edges,
            ConnectedCount = Math.Max(bfs.Hops.Count - 1, 0),
            DownCount = nodes.Count(n => n.IsDown && !n.IsGateway),
            CutOffCount = nodes.Count(n => !n.IsGateway && !n.IsDown && n.Hops < 0),
            MstTotalCost = Math.Round(mst.TotalCost, 1),
            FullMeshCost = Math.Round(fullCost, 1),
            SavingsPercent = fullCost > 0 ? Math.Round((1 - mst.TotalCost / fullCost) * 100, 1) : 0,
            PrimMicroseconds = primMicros,
            BfsMicroseconds = bfsMicros,
            Reroutes = reroutes,
            ComputedUtc = DateTime.UtcNow
        };
    }

    // ======== Helpers =========

    private static Dictionary<string, string> NameMap(List<DeviceStateDto> devices)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [GatewayMac] = "Gateway Hub" };
        foreach (var d in devices) names[d.MacAddress] = d.Name;
        return names;
    }

    private static string NameOf(Dictionary<string, string> names, string mac) =>
        names.TryGetValue(mac, out var name) ? name : mac;

    private static string Describe(List<string> path, Dictionary<string, string> names) =>
        string.Join(" → ", path.Select(m => names.TryGetValue(m, out var name) ? name : m));

    private static string EdgeKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0 ? a + "|" + b : b + "|" + a;
}