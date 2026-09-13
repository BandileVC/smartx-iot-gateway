namespace SmartX.Shared.Models;

/// <summary>
/// Represents a single smart-meter power reading in watts.
/// Operator overloads let the engineering team write natural aggregation and
/// delta-comparison code directly against domain objects, e.g.:
///     var meter3 = meter1 + meter2;      // aggregate load of two meters
///     if (meter3 > threshold) { ... }    // anomaly/spike comparison
///     var delta = readingNow - readingPrevious; // rate-of-change / spike detection
/// </summary>
public readonly struct PowerReading : IComparable<PowerReading>
{
    public string MeterId { get; }
    public double Watts { get; }
    public DateTime TimestampUtc { get; }

    public PowerReading(string meterId, double watts, DateTime? timestampUtc = null)
    {
        MeterId = meterId;
        Watts = watts;
        TimestampUtc = timestampUtc ?? DateTime.UtcNow;
    }

    // Aggregate two meters into a synthetic combined-load reading.
    public static PowerReading operator +(PowerReading a, PowerReading b) =>
        new($"{a.MeterId}+{b.MeterId}", a.Watts + b.Watts);

    // Delta between two readings - used for spike/anomaly detection.
    public static PowerReading operator -(PowerReading a, PowerReading b) =>
        new($"{a.MeterId}-{b.MeterId}", a.Watts - b.Watts);

    public static bool operator >(PowerReading a, PowerReading b) => a.Watts > b.Watts;
    public static bool operator <(PowerReading a, PowerReading b) => a.Watts < b.Watts;
    public static bool operator >=(PowerReading a, PowerReading b) => a.Watts >= b.Watts;
    public static bool operator <=(PowerReading a, PowerReading b) => a.Watts <= b.Watts;

    public static bool operator ==(PowerReading a, PowerReading b) =>
        Math.Abs(a.Watts - b.Watts) < 0.0001 && a.MeterId == b.MeterId;

    public static bool operator !=(PowerReading a, PowerReading b) => !(a == b);

    public int CompareTo(PowerReading other) => Watts.CompareTo(other.Watts);

    public override bool Equals(object? obj) => obj is PowerReading other && this == other;
    public override int GetHashCode() => HashCode.Combine(MeterId, Watts);
    public override string ToString() => $"{MeterId}: {Watts:F2}W @ {TimestampUtc:HH:mm:ss}";
}
