namespace SmartX.Shared.Models;

public enum AlertSeverity { Critical = 0, Warning = 1 }
public enum DeviceStatus { Online = 0, Warning = 1, Offline = 2, Critical = 3 }
public enum InsightKind { LearnedPattern, PredictedBreach, NeedsAttention }

public class DeviceStateDto
{
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public SensorCategory Category { get; set; }
    public string Location { get; set; } = "";
    public DeviceStatus Status { get; set; } = DeviceStatus.Online;
    public double? LatestValue { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public bool ActuatorOn { get; set; }
    public long PacketCount { get; set; }
    public DeviceStateDto Clone() => (DeviceStateDto)MemberwiseClone();
}

public class DeviceRegisterRequest
{
    public string MacAddress { get; set; } = "";
    public string? Name { get; set; }
    public SensorCategory Category { get; set; }
    public string? Location { get; set; }
}

/// <summary>What a device (or the simulator) POSTs to /api/stream/ingest.</summary>
public class DeviceTelemetryRequest
{
    public string MacAddress { get; set; } = "";
    public string? Name { get; set; }
    public SensorCategory Category { get; set; }
    public double Value { get; set; }          
    public DateTime? TimestampUtc { get; set; }
}

public class StreamReadingDto
{
    public long Sequence { get; set; }
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public SensorCategory Category { get; set; }
    public double Value { get; set; }
    public DateTime TimestampUtc { get; set; }
    public AlertSeverity? Severity { get; set; }
    public string Lane { get; set; } = "Standard";   
    public double QueueLatencyMs { get; set; }
    public DeviceStateDto? Device { get; set; }
}

public class TimelinePointDto
{
    public DateTime TimestampUtc { get; set; }
    public double Value { get; set; }
}

public class AlertDto
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public DateTime RaisedUtc { get; set; }
}

public class CommandRequest
{
    public string MacAddress { get; set; } = "";
    public string Action { get; set; } = "TOGGLE";   
    public string Source { get; set; } = "Operator"; 
}

public class BulkCommandRequest
{
    public string Action { get; set; } = "OFF";
    public string? NameContains { get; set; }        
}

public class StateChangeDto
{
    public string MacAddress { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool PreviousOn { get; set; }
    public bool NewOn { get; set; }
}

public class CommandRecordDto
{
    public long Id { get; set; }
    public DateTime IssuedUtc { get; set; }
    public string Description { get; set; } = "";
    public string Action { get; set; } = "";
    public string Source { get; set; } = "Operator";
    public bool IsBulk { get; set; }
    public bool IsUndo { get; set; }
    public List<StateChangeDto> Changes { get; set; } = new();
}

public class CommandResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public CommandRecordDto? Record { get; set; }
}

public class CommandHistoryDto
{
    public int UndoDepth { get; set; }
    public CommandRecordDto? NextUndo { get; set; }
    public List<CommandRecordDto> Audit { get; set; } = new();  
}

public class StreamStatsDto
{
    public int StandardQueueDepth { get; set; }
    public int PriorityQueueDepth { get; set; }
    public long TotalEnqueued { get; set; }
    public long TotalProcessed { get; set; }
    public long PriorityProcessed { get; set; }
    public long StandardProcessed { get; set; }
    public long Rejected { get; set; }
    public double AvgPriorityLatencyMs { get; set; }
    public double AvgStandardLatencyMs { get; set; }
    public int ActiveAlerts { get; set; }
    public long DuplicateAlertsSuppressed { get; set; }
    public int RegisteredDevices { get; set; }
    public int UndoDepth { get; set; }
}

public class SuggestedActionDto
{
    public string Id { get; set; } = "";
    public InsightKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Confidence { get; set; }
    public double Score { get; set; }
    public string TargetMac { get; set; } = "";
    public string? SuggestedCommand { get; set; }    
}

public class SearchEventRequest { public string Term { get; set; } = ""; }