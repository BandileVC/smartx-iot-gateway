namespace SmartX.Shared.Models;

public class HistoryQueryResultDto
{
    public string MacAddress { get; set; } = "";
    public int TotalStored { get; set; }
    public int Returned { get; set; }
    public bool Truncated { get; set; }
    public double QueryMicroseconds { get; set; }
    public int TreeHeight { get; set; }
    public List<TimelinePointDto> Points { get; set; } = new();
}

public class TreeBenchmarkDto
{
    public int Count { get; set; }
    public int WindowMatches { get; set; }

    public double AvlInsertMs { get; set; }
    public double AvlRangeMicros { get; set; }
    public int AvlHeight { get; set; }
    public int MaxPossibleAvlHeight { get; set; }
    public long AvlRotations { get; set; }
    public bool AvlBalanced { get; set; }

    public double ListInsertMs { get; set; }
    public double ListScanMicros { get; set; }
    public double RangeSpeedupVsList { get; set; }

    public int BstCount { get; set; }
    public double BstInsertMs { get; set; }
    public double BstRangeMicros { get; set; }
    public double AvlInsertMsAtBstCount { get; set; }
}