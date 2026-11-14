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

public class MeshNodeDto
{
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public SensorCategory Category { get; set; }
    public bool IsGateway { get; set; }
    public bool IsDown { get; set; }
    public string? DownReason { get; set; }
    public double X { get; set; }                 
    public double Y { get; set; }
    public int Hops { get; set; } = -1;           // BFS hops from gateway, -1 = unreachable
    public int Dependents { get; set; }           
}

public class MeshEdgeDto
{
    public string FromMac { get; set; } = "";
    public string ToMac { get; set; } = "";
    public double Cost { get; set; }
    public bool Active { get; set; }              // both endpoints up
    public bool InMst { get; set; }
}

public class RerouteDto
{
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public string OldRoute { get; set; } = "";
    public string NewRoute { get; set; } = "";
    public string Status { get; set; } = "Rerouted";   // "Rerouted" 
}

public class MeshAnalysisDto
{
    public string GatewayMac { get; set; } = "";
    public List<MeshNodeDto> Nodes { get; set; } = new();
    public List<MeshEdgeDto> Edges { get; set; } = new();
    public int ConnectedCount { get; set; }
    public int DownCount { get; set; }
    public int CutOffCount { get; set; }
    public double MstTotalCost { get; set; }
    public double FullMeshCost { get; set; }
    public double SavingsPercent { get; set; }
    public double PrimMicroseconds { get; set; }
    public double BfsMicroseconds { get; set; }
    public List<RerouteDto> Reroutes { get; set; } = new();
    public DateTime ComputedUtc { get; set; }
}

public class RouteDto
{
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Found { get; set; }
    public List<string> MstPath { get; set; } = new();
    public string MstRoute { get; set; } = "";
    public double MstCost { get; set; }
    public int MstHops { get; set; }
    public List<string> BfsPath { get; set; } = new();
    public string BfsRoute { get; set; } = "";
    public double BfsCost { get; set; }
    public int BfsHops { get; set; }
}

public class ConnectedNodeDto
{
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public int Hops { get; set; }
}

public class ConnectedDto
{
    public string StartMac { get; set; } = "";
    public List<ConnectedNodeDto> Connected { get; set; } = new();
    public List<ConnectedNodeDto> Down { get; set; } = new();
    public List<ConnectedNodeDto> CutOff { get; set; } = new();
}