namespace SmartX.Shared.Models;

/// <summary>
/// Generic wrapper for disparate incoming telemetry payloads (float soil-moisture,
/// int power-wattage, bool valve-state) without resorting to boxing/unboxing.
///
/// Because T is constrained to `struct`, the CLR stores the Value field inline as
/// part of TelemetryPacket&lt;T&gt; itself (a value type), rather than allocating a
/// separate object on the heap and boxing the primitive into it. This keeps the
/// high-throughput ingestion path (thousands of ESP32 devices publishing constantly)
/// allocation-light.
/// </summary>
/// <typeparam name="T">The underlying primitive payload type (float, int, bool, etc.)</typeparam>
public readonly struct TelemetryPacket<T> where T : struct
{
    public string SensorId { get; }
    public T Value { get; }
    public DateTime TimestampUtc { get; }
    public SensorCategory Category { get; }

    public TelemetryPacket(string sensorId, T value, SensorCategory category, DateTime? timestampUtc = null)
    {
        SensorId = sensorId;
        Value = value;
        Category = category;
        TimestampUtc = timestampUtc ?? DateTime.UtcNow;
    }

    public override string ToString() =>
        $"[{TimestampUtc:HH:mm:ss.fff}] {SensorId} ({Category}) => {Value}";
}
