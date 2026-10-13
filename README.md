## Part 2 Real-Time Command Stream

### Data structures
| Structure | Class | Purpose |
|
| `Queue<T>` and `TelemetryPipeline` | FIFO lane for normal telemetry |
| `PriorityQueue<T,(int,long)>` and `TelemetryPipeline` | Critical alerts bypass the FIFO backlog |
| `Stack<T>` and `CommandService` | Command history with Undo (single and bulk) |
| `Dictionary<string,DeviceStateDto>` and `DeviceRegistry` | Live devices keyed by MAC, O(1) lookup |
| `SortedDictionary<DateTime,double>` and `TimelineStore` | Time-ordered logs per device, feeds timeline graph |
| `HashSet<string>` and `AlertTracker` | Unique active alerts, duplicates suppressed |
| Dictionary of dictionaries + sliding `Queue` + `PriorityQueue` | `PredictionEngine` | Association-rule learning and ranked suggestions |

### Run this
```bash
dotnet restore && dotnet build
dotnet run --project SmartX.Api                      
dotnet run --project SmartX.Client                  
dotnet run --project SmartX.Simulator -- run         
```

### How to test 
1. Dictionary (MAC lookup): with the simulator running, open Command Stream;
   Manual ingest by MAC:
   `curl -X POST http://localhost:5050/api/stream/ingest -H "Content-Type: application/json" -d '{"macAddress":"AA:BB:CC:00:00:01","category":0,"value":50}'`
   then `curl http://localhost:5050/api/devices`.
2. FIFO queue: watch Standard queue and Processed on the stats strip; `curl http://localhost:5050/api/stream/stats`.
3. High-priority alert, which shows the priority queue bypassing the backlog:
```bash
   dotnet run --project SmartX.Simulator -- flood 800     
   dotnet run --project SmartX.Simulator -- critical      
```
   In Live stream the red Priority row is processed ahead of earlier-queued Standard rows (compare `#sequence`).
   Avg latency: priority stays in single-digit ms while standard grows with the backlog.
4. Stack / Undo: press **ON** on Pump A, then Close all valves, then Undo last twice; each undo pops the stack and restores previous states. Undo entries appear in the history.
5. HashSet (duplicate suppression): stop the simulator. After ~20 s each device raises exactly one `DISCONNECTED` alert; Duplicate alerts suppressed then climbs every 5 s as the watchdog re-attempts and the set rejects.
6. SortedDictionary: click a device card; the timeline loads from `GET /api/stream/timeline/{mac}` (already time-ordered).
7. Predictive engine:
   - Run the simulator; within seconds Moisture Sensor B shows a projected breach (regression forecast).
   - When it goes critical, search Pump (press Enter), turn Pump A ON, let it recover, turn it OFF; repeat twice.
   - On the next critical alert a Turn ON Pump A suggestion appears with its confidence. Press Apply.
   - Shortcut for demos: while Sensor B is critical, press Train demo history.