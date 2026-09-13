using System.Collections.Concurrent;
using System.Diagnostics;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;

namespace SmartX.Api.Data;

/// <summary>
/// Central state for the Smart-X gateway simulation. Thread-safe (ConcurrentDictionary +
/// Interlocked counters) since telemetry can arrive from many simulated devices concurrently.
/// </summary>
public class InMemoryStore
{
    public ConcurrentDictionary<string, SensorProfile> Sensors { get; } = new();

    // One custom TelemetryHistory<T> BST per sensor, per payload type it uses.
    public ConcurrentDictionary<string, TelemetryHistory<float>> FloatHistories { get; } = new();
    public ConcurrentDictionary<string, TelemetryHistory<int>> IntHistories { get; } = new();
    public ConcurrentDictionary<string, TelemetryHistory<bool>> BoolHistories { get; } = new();

    public DeploymentNode DeploymentRoot { get; } = SeedDeploymentTree();

    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    private long _generated;
    private long _transmitted;
    private long _received;
    private long _stored;

    public string? LastSensorId { get; private set; }
    public double? LastReadingValue { get; private set; }
    public DateTime? LastReadingUtc { get; private set; }
    public double? LastInsertMicroseconds { get; private set; }
    public double? LastSearchMicroseconds { get; private set; }

    public long Generated => _generated;
    public long Transmitted => _transmitted;
    public long Received => _received;
    public long Stored => _stored;

    public void RecordGenerated(int count = 1) => Interlocked.Add(ref _generated, count);
    public void RecordTransmitted(int count = 1) => Interlocked.Add(ref _transmitted, count);

    public void RecordReceived(string sensorId, double value, DateTime timestampUtc)
    {
        Interlocked.Increment(ref _received);
        LastSensorId = sensorId;
        LastReadingValue = value;
        LastReadingUtc = timestampUtc;
    }

    public void RecordStored(long insertTicks)
    {
        Interlocked.Increment(ref _stored);
        LastInsertMicroseconds = insertTicks * 1_000_000.0 / Stopwatch.Frequency;
    }

    public void RecordSearch(long searchTicks) =>
        LastSearchMicroseconds = searchTicks * 1_000_000.0 / Stopwatch.Frequency;

    private static DeploymentNode SeedDeploymentTree() => new()
    {
        Name = "Root",
        Children = new List<DeploymentNode>
        {
            new()
            {
                Name = "Facility A",
                Children = new List<DeploymentNode>
                {
                    new()
                    {
                        Name = "Zone 1",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Sub-Zone B", HostsSensor = true },
                            new() { Name = "Sub-Zone C", HostsSensor = true },
                        }
                    },
                    new()
                    {
                        Name = "Zone 2",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Sub-Zone A", HostsSensor = true },
                        }
                    }
                }
            },
            new()
            {
                Name = "Facility B",
                Children = new List<DeploymentNode>
                {
                    new()
                    {
                        Name = "Zone 1",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Sub-Zone A", HostsSensor = true },
                        }
                    }
                }
            }
        }
    };
}
