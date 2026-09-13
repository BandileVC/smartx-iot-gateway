using SmartX.Shared.Models;

namespace SmartX.Shared.Services;

/// <summary>
/// Simulates a batch of raw telemetry arriving from multiple ESP32-class devices.
/// Raw readings are first captured into a JAGGED ARRAY (float[][]) — one row per
/// sensor, each row holding a variable number of raw readings for that sensor in
/// this batch — mirroring how a real gateway buffers uneven per-device publish
/// rates before normalising. Only after this raw capture stage are the values
/// transferred into the optimised List&lt;T&gt; / TelemetryHistory&lt;T&gt; collections
/// used by the rest of the system.
/// </summary>
public static class BatchGenerator
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Generates a jagged array of raw float readings: readings[sensorIndex][sampleIndex].
    /// Each simulated sensor produces a random number of samples (2-6) to mimic
    /// uneven publish cadence across the mesh.
    /// </summary>
    public static float[][] GenerateRawEnvironmentalBatch(int sensorCount, float baseline = 45f, float noise = 8f)
    {
        var batch = new float[sensorCount][];
        for (int i = 0; i < sensorCount; i++)
        {
            int sampleCount = Rng.Next(2, 7);
            batch[i] = new float[sampleCount];
            for (int j = 0; j < sampleCount; j++)
            {
                // Occasionally inject an anomalous spike (~8% chance) to exercise
                // the dashboard's live anomaly-highlighting engagement feature.
                bool spike = Rng.NextDouble() < 0.08;
                float value = baseline + (float)(Rng.NextDouble() * 2 - 1) * noise;
                if (spike) value += noise * (Rng.Next(0, 2) == 0 ? 4f : -4f);
                batch[i][j] = MathF.Round(value, 2);
            }
        }
        return batch;
    }

    /// <summary>
    /// Flattens a jagged raw batch into the optimised List&lt;TelemetryPacket&lt;float&gt;&gt;
    /// used downstream for ingestion, tagging each reading with a sensor id.
    /// </summary>
    public static List<TelemetryPacket<float>> FlattenToPackets(float[][] jaggedBatch, IReadOnlyList<string> sensorIds, SensorCategory category)
    {
        var packets = new List<TelemetryPacket<float>>();
        for (int i = 0; i < jaggedBatch.Length; i++)
        {
            string sensorId = i < sensorIds.Count ? sensorIds[i] : $"SIM-{i}";
            foreach (var reading in jaggedBatch[i])
            {
                packets.Add(new TelemetryPacket<float>(sensorId, reading, category));
            }
        }
        return packets;
    }
}
