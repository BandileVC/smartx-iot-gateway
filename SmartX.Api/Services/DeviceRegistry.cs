using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public record ActuatorChange(DeviceStateDto Device, bool Previous, bool Changed);


public class DeviceRegistry
{
    private readonly Dictionary<string, DeviceStateDto> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public static string NormalizeMac(string mac) => mac.Trim().ToUpperInvariant();

    public int Count { get { lock (_gate) return _devices.Count; } }

    public DeviceStateDto Register(string mac, string? name, SensorCategory category, string? location)
    {
        mac = NormalizeMac(mac);
        lock (_gate)
        {
            if (!_devices.TryGetValue(mac, out var d))
            {
                d = new DeviceStateDto
                {
                    MacAddress = mac,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Node {(mac.Length > 5 ? mac[^5..] : mac)}" : name!,
                    Category = category,
                    Location = location ?? ""
                };
                _devices[mac] = d;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(name)) d.Name = name!;
                if (!string.IsNullOrWhiteSpace(location)) d.Location = location!;
            }
            return d.Clone();
        }
    }


    public DeviceStateDto RegisterFromSensor(SensorProfile p) =>
        Register(string.IsNullOrWhiteSpace(p.MacAddress) ? p.SensorId : p.MacAddress,
                 $"Sensor {p.SensorId}", p.Category, p.DeploymentLocation);

    public bool TryGet(string mac, out DeviceStateDto device)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(NormalizeMac(mac), out var d)) { device = d.Clone(); return true; }
        }
        device = null!;
        return false;
    }

    public DeviceStateDto? ApplyReading(string mac, double value, AlertSeverity? severity)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(mac, out var d)) return null;   
            var first = d.PacketCount == 0;
            d.LatestValue = value;
            d.LastSeenUtc = DateTime.UtcNow;
            d.PacketCount++;
            d.Status = severity switch
            {
                AlertSeverity.Critical => DeviceStatus.Critical,
                AlertSeverity.Warning => DeviceStatus.Warning,
                _ => DeviceStatus.Online
            };
            if (d.Category == SensorCategory.Actuator && first) d.ActuatorOn = value >= 0.5;
            return d.Clone();
        }
    }

    public List<DeviceStateDto> Query(string? query, SensorCategory? category, bool alertsOnly)
    {
        lock (_gate)
            return _devices.Values
                .Where(d => DeviceFilter.Matches(d, query, category, alertsOnly))
                .OrderByDescending(d => (int)d.Status).ThenBy(d => d.Name)
                .Select(d => d.Clone()).ToList();
    }

    public List<DeviceStateDto> All() => Query(null, null, false);

    public List<DeviceStateDto> Actuators(string? nameContains)
    {
        lock (_gate)
            return _devices.Values
                .Where(d => d.Category == SensorCategory.Actuator &&
                            (string.IsNullOrWhiteSpace(nameContains) ||
                             d.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)))
                .Select(d => d.Clone()).ToList();
    }

    public ActuatorChange? SetActuator(string mac, bool on)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(NormalizeMac(mac), out var d) || d.Category != SensorCategory.Actuator)
                return null;
            var previous = d.ActuatorOn;
            d.ActuatorOn = on;
            return new ActuatorChange(d.Clone(), previous, previous != on);
        }
    }

    public List<DeviceStateDto> StaleDevices(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        lock (_gate)
            return _devices.Values.Where(d => d.LastSeenUtc is not null && d.LastSeenUtc < cutoff)
                .Select(d => d.Clone()).ToList();
    }

    public bool MarkOffline(string mac)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(mac, out var d) || d.Status == DeviceStatus.Offline) return false;
            d.Status = DeviceStatus.Offline;
            return true;
        }
    }
}