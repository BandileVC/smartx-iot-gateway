namespace SmartX.Shared.Models;

public enum PayloadType { Float, Int, Bool }

public class RegisterSensorRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeploymentLocation { get; set; } = string.Empty; // "Facility A/Zone 1/Sub-Zone B"
    public SensorCategory Category { get; set; }
}

public class IngestTelemetryRequest
{
    public string SensorId { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public PayloadType PayloadType { get; set; }
    public float? FloatValue { get; set; }
    public int? IntValue { get; set; }
    public bool? BoolValue { get; set; }
    public DateTime? TimestampUtc { get; set; }
}

public class GenerateBatchRequest
{
    public int SensorCount { get; set; } = 5;
    public List<string>? SensorIds { get; set; }
}

public class TelemetryReadingDto
{
    public string SensorId { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public double NumericValue { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool IsAnomaly { get; set; }
}

public class ApiStatusDto
{
    public bool Online { get; set; } = true;
    public TimeSpan Uptime { get; set; }
    public int TotalRegisteredSensors { get; set; }
    public long TelemetryGenerated { get; set; }
    public long TelemetryTransmitted { get; set; }
    public long TelemetryReceived { get; set; }
    public long TotalRecordsStored { get; set; }
    public string? LastSensorId { get; set; }
    public double? LastReadingValue { get; set; }
    public DateTime? LastReadingUtc { get; set; }
    public string SearchAlgorithm { get; set; } = "Binary Search Tree (custom, timestamp-keyed)";
    public double? LastInsertMicroseconds { get; set; }
    public double? LastSearchMicroseconds { get; set; }
}

public class DeploymentValidationRequest
{
    public List<string> Path { get; set; } = new();
}
