namespace SmartX.Shared.Models;

public static class DeviceFilter
{
    public static bool Matches(DeviceStateDto d, string? query, SensorCategory? category, bool alertsOnly)
    {
        if (category is not null && d.Category != category) return false;
        if (alertsOnly && d.Status == DeviceStatus.Online) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;

        var q = query.Trim();
        return d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || d.MacAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
            || d.Location.Contains(q, StringComparison.OrdinalIgnoreCase)
            || d.Category.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}