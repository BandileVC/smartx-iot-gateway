using System.Globalization;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class MeshEndpoints
{
    public static void MapMeshEndpoints(this WebApplication app)
    {
        // ---- Historical range query (AVL tree) ----
        app.MapGet("/api/mesh/history/{mac}", (string mac, string? from, string? to, int? limit, HistoryStore history) =>
        {
            var fromUtc = ParseUtc(from) ?? DateTime.UtcNow.AddHours(-1);
            var toUtc = ParseUtc(to) ?? DateTime.UtcNow;
            var max = Math.Clamp(limit ?? 500, 1, 5000);
            return Results.Ok(history.Query(DeviceRegistry.NormalizeMac(mac), fromUtc, toUtc, max));
        });

        app.MapGet("/api/mesh/history/{mac}/latest", (string mac, int? count, HistoryStore history) =>
            Results.Ok(history.Latest(DeviceRegistry.NormalizeMac(mac), Math.Clamp(count ?? 100, 1, 5000))));

        // ---- Benchmark: AVL vs list scan vs Part 1 BST ----
        app.MapPost("/api/mesh/benchmark", (int? count) =>
            Results.Ok(TreeBenchmark.Run(Math.Clamp(count ?? 50000, 1000, 500000))));

        // (Step 6 adds the graph endpoints here)
    }

    // Parsed as UTC explicitly. Binding a DateTime straight from the query string converts "Z" times
    // to the server's local time zone, which would shift the query window.
    private static DateTime? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
            return value;
        return null;
    }
}