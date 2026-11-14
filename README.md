# Part 3: Network Topology and Mesh Routing

# Quick start: 
```bash
# prerequisites: .NET 10 SDK (and optionally Docker Desktop)
dotnet restore
dotnet build
dotnet run --project SmartX.Api                 # terminal 1: http://localhost:5050
dotnet run --project SmartX.Client              # terminal 2: open the printed URL
dotnet run --project SmartX.Simulator -- run    # terminal 3: virtual ESP32 fleet
# or: docker compose --profile sim up --build   (client on http://localhost:8080)
```
Open **Mesh Routing**. Let the simulator run about a minute so history accumulates.

# Using the Mesh Module

1. **Map:** Nodes in the ESP32 mesh network are visualized. Lines represent links between nodes. Each link has a power cost that can be displayed.

2. **Track a route:** To track a route for a specific node in the mesh, simply input the node’s MAC address or name and press Enter. A visualized path to the gateway appears on the map, representing the lowest power route and the table also presents alternative paths to the gateway.

3. **Self-healing:** If a node is in the “relays for N nodes” state, that node can be selected and the “Simulate failure” button pressed. A visual representation of the failover plan is generated, showcasing the new path for each node affected by the simulated failure. Pressing the “Stop simulator” button automatically removes those nodes from the mesh network.

4. **History log:** To review past telemetry data of a specific node, select a time range within the “History log” and press the “Query range” button.

5. **Benchmark:** Compare the AVL tree data structure with three other data structures in the system: AVL tree, a linear list and an unbalanced BST.

# Data structures and their role

The `SmartX.Shared/Collections/AvlTree.cs` file within the Mesh Module’s project defines the AVL tree data structure used in the system. Historical telemetry data for each sensor within the network is stored in the form of an AVL tree that is keyed by timestamps from the sensors. Since timestamps are ordered in increasing amounts as sensors transmit data, the creation of an unbalanced BST would take an O(n) amount of time to complete. The AVL tree utilizes balancing rotations in order to maintain an even height difference of at most one level for the left and right subtrees of any given node in the tree.
```csharp
private Node Rebalance(Node node)
{
    UpdateHeight(node);
    var balance = BalanceFactor(node);
    if (balance > 1)  { if (BalanceFactor(node.Left!) < 0)  node.Left  = RotateLeft(node.Left!);  return RotateRight(node); }
    if (balance < -1) { if (BalanceFactor(node.Right!) > 0) node.Right = RotateRight(node.Right!); return RotateLeft(node); }
    return node;
}
```
Range queries prune whole subtrees, giving O(log n + k) for k matches:
```csharp
if (aboveFrom) RangeWalk(node.Left, ...);       
if (aboveFrom && belowTo) results.Add(...);
if (belowTo)   RangeWalk(node.Right, ...);      
```

# Graph (adjacency list): 
`MeshGraph.cs` The graph includes the nodes as well as the gateway. The edges represent the wireless links between nodes and the weight of each edge represents the transmit power, which is proportional to the distance squared between nodes. The adjacency list data structure is chosen to represent this sparse graph. This allows the same data structure to model both the physical mesh network and the health of the nodes in that network.

# Breadth-First Search 
BFS is used to traverse the graph to find the nodes that are connected to the gateway and a FIFO queue is used in BFS. This ensures that the nodes are visited in the order of the number of hops from the gateway. The BFS algorithm will return all the nodes that are connected to the gateway along with the number of hops from the gateway to each node. 
```csharp
while (queue.Count > 0)
{
    var current = queue.Dequeue();
    foreach (var link in _adjacency[current])
        if (!visited.Contains(link.To) && !IsDown(link.To)) { visited.Add(link.To); queue.Enqueue(link.To); }
}
```

# Prim's MST with a min-heap
The gateway serves as the root of the tree. A `PriorityQueue` is utilized to extract the edge with the lowest weight that connects to a node outside of the tree and the time complexity of this algorithm is O(E log E). 
```csharp
while (heap.TryDequeue(out var edge, out _))
{
    if (inTree.Contains(edge.To)) continue;     // would form a cycle
    inTree.Add(edge.To);
    result.Parent[edge.To] = edge.From;         
    ...
}
```

# Self-healing
The watchdog marks the silent devices as offline. The `MeshService` updates the graph to mark the silent devices as down and the BFS and Prim algorithms are re-run to determine the new routes. Additionally, the `ReceiveTopology` method publishes the new routes to the network via SignalR so a signature check ensures that the clients are notified of the topology change only if the graph has actually changed.

# Performance and how it scales
Run: `curl.exe -X POST "http://localhost:5050/api/mesh/benchmark?count=100000"` or use the Benchmark card.

| Readings (n) | AVL max height 1.44·log₂(n+2) | AVL height (measured) | AVL 1% range (µs) | List scan 1% range (µs) | Speed-up |
|---|---|---|---|---|---|
| 10,000 | 20 | *(14)* | *(15,1)* | *(90,8)* | *(6)* |
| 100,000 | 24 | *(17)* | *(70,7)* | *(819,8)* | *(11,6)* |
| 500,000 | 27 | *(19)* | *(1187,1)* | *(3655,8)* | *(3,1)* |



# Testing guide
1. `GET /api/mesh/topology`: nodes, edges, MST flags.
2. `GET /api/mesh/route/{mac}`: MST and BFS routes.
3. `POST /api/mesh/nodes/{mac}/fail`, then check `reroutes` in `/api/mesh/topology`; `POST .../restore-all` to undo.
4. `GET /api/mesh/history/{mac}?from=...&to=...`: AVL range query.
5. `POST /api/mesh/benchmark?count=...`: scaling.

# Known limitations
The history is stored in memory, which means that the data is lost upon restarting the system. The link costs are based on the geometry of the network instead of the RSSI measurements of the links. 