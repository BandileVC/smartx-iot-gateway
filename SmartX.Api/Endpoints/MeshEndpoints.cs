using System.Globalization;
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class MeshEndpoints
{
    public static void MapMeshEndpoints(this WebApplication app)
    {
        
        app.MapGet("/api/mesh/history/{mac}", (string mac, string? from, string? to, int? limit, HistoryStore history) =>
        {
            var fromUtc = ParseUtc(from) ?? DateTime.UtcNow.AddHours(-1);
            var toUtc = ParseUtc(to) ?? DateTime.UtcNow;
            var max = Math.Clamp(limit ?? 500, 1, 5000);
            return Results.Ok(history.Query(DeviceRegistry.NormalizeMac(mac), fromUtc, toUtc, max));
        });

        app.MapGet("/api/mesh/history/{mac}/latest", (string mac, int? count, HistoryStore history) =>
            Results.Ok(history.Latest(DeviceRegistry.NormalizeMac(mac), Math.Clamp(count ?? 100, 1, 5000))));

        
        app.MapPost("/api/mesh/benchmark", (int? count) =>
            Results.Ok(TreeBenchmark.Run(Math.Clamp(count ?? 50000, 1000, 500000))));

                
        app.MapGet("/api/mesh/topology", (MeshService mesh) => Results.Ok(mesh.Analyze()));

        app.MapGet("/api/mesh/connected", (string? from, MeshService mesh) => Results.Ok(mesh.Connected(from)));

        app.MapGet("/api/mesh/route/{mac}", (string mac, MeshService mesh) =>
        {
            var route = mesh.Route(mac);
            return route is null ? Results.NotFound(new { error = "Unknown node." }) : Results.Ok(route);
        });

        app.MapPost("/api/mesh/nodes/{mac}/fail", async (string mac, DeviceRegistry registry, MeshService mesh) =>
        {
            if (!registry.TryGet(mac, out _)) return Results.NotFound(new { error = "Unknown node." });
            if (!mesh.SetManualDown(mac, true)) return Results.BadRequest(new { error = "The gateway cannot be failed." });
            await mesh.BroadcastIfChangedAsync(true);
            return Results.Ok(mesh.Analyze());
        });

        app.MapPost("/api/mesh/nodes/{mac}/restore", async (string mac, MeshService mesh) =>
        {
            mesh.SetManualDown(mac, false);
            await mesh.BroadcastIfChangedAsync(true);
            return Results.Ok(mesh.Analyze());
        });

        app.MapPost("/api/mesh/restore-all", async (MeshService mesh) =>
        {
            mesh.RestoreAll();
            await mesh.BroadcastIfChangedAsync(true);
            return Results.Ok(mesh.Analyze());
        });
    }

    
    private static DateTime? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
            return value;
        return null;
    }
}