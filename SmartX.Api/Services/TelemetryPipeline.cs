using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public record QueuedPacket(
    long Sequence, string Mac, SensorCategory Category, double Value,
    DateTime TimestampUtc, DateTime EnqueuedUtc,
    AlertSeverity? Severity, string? AlertType, string? AlertMessage)
{
    public bool IsPriority => Severity == AlertSeverity.Critical;
}

public class TelemetryPipeline
{
    private readonly Queue<QueuedPacket> _standard = new();
    private readonly PriorityQueue<QueuedPacket, (int Rank, long Seq)> _priority = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0);   
    private readonly int _maxStandard;
    private long _sequence;
    private long _totalEnqueued;

    public TelemetryPipeline(IConfiguration config) =>
        _maxStandard = config.GetValue("Pipeline:MaxStandardQueueDepth", 5000);

    public long TotalEnqueued { get { lock (_gate) return _totalEnqueued; } }

    public (int Standard, int Priority) Depths() { lock (_gate) return (_standard.Count, _priority.Count); }

    public QueuedPacket? Enqueue(string mac, SensorCategory category, double value, DateTime ts,
        AlertSeverity? severity, string? alertType, string? alertMessage)
    {
        QueuedPacket packet;
        lock (_gate)
        {
            var seq = ++_sequence;
            packet = new QueuedPacket(seq, mac, category, value, ts, DateTime.UtcNow, severity, alertType, alertMessage);

            if (packet.IsPriority)
                _priority.Enqueue(packet, ((int)(severity ?? AlertSeverity.Critical), seq));
            else
            {
                if (_standard.Count >= _maxStandard) return null;
                _standard.Enqueue(packet);
            }
            _totalEnqueued++;
        }
        _signal.Release();
        return packet;
    }

    /// <summary>Priority lane is always drained before the standard lane.</summary>
    public async Task<QueuedPacket?> DequeueAsync(CancellationToken ct)
    {
        await _signal.WaitAsync(ct);
        lock (_gate)
        {
            if (_priority.TryDequeue(out var urgent, out _)) return urgent;
            return _standard.TryDequeue(out var normal) ? normal : null;
        }
    }
}

public class StreamMetrics
{
    private readonly object _gate = new();
    private long _processed, _priorityProcessed, _standardProcessed, _rejected;
    private double _priorityLatencySum, _standardLatencySum;

    public void RecordProcessed(bool priority, double latencyMs)
    {
        lock (_gate)
        {
            _processed++;
            if (priority) { _priorityProcessed++; _priorityLatencySum += latencyMs; }
            else { _standardProcessed++; _standardLatencySum += latencyMs; }
        }
    }

    public void RecordRejected() { lock (_gate) _rejected++; }

    public (long Processed, long Priority, long Standard, long Rejected, double AvgPriority, double AvgStandard) Snapshot()
    {
        lock (_gate)
            return (_processed, _priorityProcessed, _standardProcessed, _rejected,
                _priorityProcessed == 0 ? 0 : _priorityLatencySum / _priorityProcessed,
                _standardProcessed == 0 ? 0 : _standardLatencySum / _standardProcessed);
    }
}