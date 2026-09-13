namespace SmartX.Shared.Models;

public class SensorProfile
{
    public string SensorId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string MacAddress { get; set; } = string.Empty;
    public string DeploymentLocation { get; set; } = string.Empty; // e.g. "Facility A/Zone 1/Sub-Zone B"
    public SensorCategory Category { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public List<AttachedFile> Attachments { get; set; } = new();
}

public class AttachedFile
{
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty; // encrypted-at-rest path
    public long SizeBytes { get; set; }
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}
