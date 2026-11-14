using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class StreamEndpoints
{
    public static void MapCommandStreamEndpoints(this WebApplication app)
    {
        
        app.MapPost("/api/devices/register", (DeviceRegisterRequest req, DeviceRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(req.MacAddress))
                return Results.BadRequest(new { error = "MacAddress is required." });
            return Results.Ok(registry.Register(req.MacAddress, req.Name, req.Category, req.Location));
        });

        app.MapGet("/api/devices", (string? query, SensorCategory? category, bool? alertsOnly, DeviceRegistry registry) =>
            Results.Ok(registry.Query(query, category, alertsOnly ?? false)));

        
        app.MapGet("/api/devices/{mac}/command", (string mac, DeviceRegistry registry) =>
            registry.TryGet(mac, out var d) ? Results.Ok(new { desiredOn = d.ActuatorOn }) : Results.NotFound());

        
        app.MapPost("/api/stream/ingest", (DeviceTelemetryRequest req, DeviceRegistry registry,
            TelemetryPipeline pipeline, StreamMetrics metrics) =>
        {
            if (string.IsNullOrWhiteSpace(req.MacAddress))
                return Results.BadRequest(new { error = "MacAddress is required." });

            var mac = DeviceRegistry.NormalizeMac(req.MacAddress);
            var device = registry.Register(mac, req.Name, req.Category, null);   
            var (severity, alertType, message) = ReadingClassifier.Classify(device.Category, req.Value, device.LatestValue);

            var packet = pipeline.Enqueue(mac, device.Category, req.Value, req.TimestampUtc ?? DateTime.UtcNow,
                severity, alertType, message);
            if (packet is null)
            {
                metrics.RecordRejected();
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            return Results.Accepted(value: new { packet.Sequence, lane = packet.IsPriority ? "Priority" : "Standard" });
        });

        app.MapGet("/api/stream/stats", (TelemetryPipeline pipeline, StreamMetrics metrics, AlertTracker alerts,
            DeviceRegistry registry, CommandService commands) =>
        {
            var (std, pri) = pipeline.Depths();
            var m = metrics.Snapshot();
            return Results.Ok(new StreamStatsDto
            {
                StandardQueueDepth = std, PriorityQueueDepth = pri, TotalEnqueued = pipeline.TotalEnqueued,
                TotalProcessed = m.Processed, PriorityProcessed = m.Priority, StandardProcessed = m.Standard,
                Rejected = m.Rejected, AvgPriorityLatencyMs = Math.Round(m.AvgPriority, 2),
                AvgStandardLatencyMs = Math.Round(m.AvgStandard, 2), ActiveAlerts = alerts.Count,
                DuplicateAlertsSuppressed = alerts.DuplicatesSuppressed, RegisteredDevices = registry.Count,
                UndoDepth = commands.UndoDepth
            });
        });

        app.MapGet("/api/stream/timeline/{mac}", (string mac, int? take, TimelineStore timeline) =>
            Results.Ok(timeline.Latest(DeviceRegistry.NormalizeMac(mac), Math.Clamp(take ?? 120, 1, 1000))));

        app.MapGet("/api/alerts", (AlertTracker alerts) => Results.Ok(alerts.Snapshot()));

        
        app.MapPost("/api/commands", async (CommandRequest req, CommandService commands) =>
        {
            var result = await commands.IssueAsync(req);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        app.MapPost("/api/commands/bulk", async (BulkCommandRequest req, CommandService commands) =>
        {
            var result = await commands.BulkAsync(req);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        app.MapPost("/api/commands/undo", async (CommandService commands) =>
        {
            var result = await commands.UndoAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        app.MapGet("/api/commands/history", (CommandService commands) => Results.Ok(commands.Snapshot()));

        
        app.MapGet("/api/insights", (PredictionEngine engine) => Results.Ok(engine.BuildInsights()));

        app.MapPost("/api/insights/search-event", (SearchEventRequest req, PredictionEngine engine) =>
        {
            var name = string.IsNullOrWhiteSpace(req.Term) ? null : engine.RecordSearch(req.Term);
            return Results.Ok(new { recorded = name is not null, device = name });
        });

        
        app.MapPost("/api/insights/seed", (DeviceRegistry registry, PredictionEngine engine) =>
        {
            var all = registry.All();
            var sensor = all.FirstOrDefault(d => d.Name == "Moisture Sensor B")
                         ?? all.FirstOrDefault(d => d.Category == SensorCategory.Environmental);
            var pump = all.FirstOrDefault(d => d.Name == "Pump A")
                       ?? all.FirstOrDefault(d => d.Category == SensorCategory.Actuator);
            if (sensor is null || pump is null)
                return Results.BadRequest(new { error = "Start the simulator first so a sensor and an actuator exist." });

            engine.SeedDemoHistory($"alert|{sensor.MacAddress}|MOISTURE_CRITICAL",
                $"view|{pump.MacAddress}", $"cmd|{pump.MacAddress}|ON", 4);
            return Results.Ok(new { seeded = true, sensor = sensor.Name, pump = pump.Name });
        });
    }
}