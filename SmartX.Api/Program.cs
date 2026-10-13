using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Data;
using SmartX.Api.Hubs;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;
using SmartX.Shared.Services;
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSignalR();
builder.Services.AddSingleton<InMemoryStore>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<TimelineStore>();
builder.Services.AddSingleton<AlertTracker>();
builder.Services.AddSingleton<StreamMetrics>();
builder.Services.AddSingleton<TelemetryPipeline>();
builder.Services.AddSingleton<PredictionEngine>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddHostedService<TelemetryProcessor>();
builder.Services.AddHostedService<DeviceWatchdog>();

var encryptionKey = builder.Configuration["FileEncryption:Key"];
if (string.IsNullOrWhiteSpace(encryptionKey))
{

    encryptionKey = FileEncryptionService.GenerateBase64Key();
    Console.WriteLine("[SmartX] No FileEncryption:Key configured - generated an ephemeral dev key.");
}
builder.Services.AddSingleton(new FileEncryptionService(encryptionKey));

builder.Services.AddCors(options =>
{
    options.AddPolicy("SmartXClient", policy =>
        policy.SetIsOriginAllowed(_ => true)   
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

app.UseCors("SmartXClient");
app.MapHub<TelemetryHub>("/hubs/telemetry");
app.MapCommandStreamEndpoints();

var uploadsDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsDir);


static bool IsAnomaly(SensorCategory category, double value) => category switch
{
    SensorCategory.Environmental => value is < 20 or > 70,   
    SensorCategory.PowerConsumption => value is < 0 or > 4000, 
    SensorCategory.Actuator => false, 
    _ => false
};



app.MapGet("/api/status", (InMemoryStore store) =>
{
    return Results.Ok(new ApiStatusDto
    {
        Online = true,
        Uptime = DateTime.UtcNow - store.StartedUtc,
        TotalRegisteredSensors = store.Sensors.Count,
        TelemetryGenerated = store.Generated,
        TelemetryTransmitted = store.Transmitted,
        TelemetryReceived = store.Received,
        TotalRecordsStored = store.Stored,
        LastSensorId = store.LastSensorId,
        LastReadingValue = store.LastReadingValue,
        LastReadingUtc = store.LastReadingUtc,
        LastInsertMicroseconds = store.LastInsertMicroseconds,
        LastSearchMicroseconds = store.LastSearchMicroseconds
    });
});

app.MapPost("/api/deployment/validate", (DeploymentValidationRequest req, InMemoryStore store) =>
{
    var result = DeploymentNode.ValidatePath(store.DeploymentRoot, req.Path);
    return Results.Ok(result);
});

app.MapGet("/api/deployment/tree", (InMemoryStore store) => Results.Ok(store.DeploymentRoot));

app.MapPost("/api/sensors/register", async (RegisterSensorRequest req, InMemoryStore store, DeviceRegistry registry) =>
{
    var segments = req.DeploymentLocation.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var validation = DeploymentNode.ValidatePath(store.DeploymentRoot, segments);
    if (!validation.IsValid)
        return Results.BadRequest(new { error = "Deployment location failed recursive validation.", validation.Message });

    var profile = new SensorProfile
    {
        MacAddress = req.MacAddress,
        DeploymentLocation = req.DeploymentLocation,
        Category = req.Category
    };

    store.Sensors[profile.SensorId] = profile;
    store.FloatHistories[profile.SensorId] = new TelemetryHistory<float>();
    store.IntHistories[profile.SensorId] = new TelemetryHistory<int>();
    store.BoolHistories[profile.SensorId] = new TelemetryHistory<bool>();

    registry.RegisterFromSensor(profile);

    await Task.CompletedTask; 
    return Results.Ok(profile);
});

app.MapGet("/api/sensors", (InMemoryStore store) => Results.Ok(store.Sensors.Values.ToList()));



app.MapPost("/api/telemetry/ingest", async (IngestTelemetryRequest req, InMemoryStore store,
    Microsoft.AspNetCore.SignalR.IHubContext<TelemetryHub> hub) =>
{
    if (!store.Sensors.ContainsKey(req.SensorId))
        return Results.NotFound(new { error = $"Unknown sensor '{req.SensorId}'." });

    var ts = req.TimestampUtc ?? DateTime.UtcNow;
    double numericValue;
    long insertTicks;

    switch (req.PayloadType)
    {
        case PayloadType.Float:
            var floatPacket = new TelemetryPacket<float>(req.SensorId, req.FloatValue ?? 0f, req.Category, ts);
            store.FloatHistories[req.SensorId].Insert(floatPacket);
            insertTicks = store.FloatHistories[req.SensorId].LastInsertTicks;
            numericValue = floatPacket.Value;
            break;
        case PayloadType.Int:
            var intPacket = new TelemetryPacket<int>(req.SensorId, req.IntValue ?? 0, req.Category, ts);
            store.IntHistories[req.SensorId].Insert(intPacket);
            insertTicks = store.IntHistories[req.SensorId].LastInsertTicks;
            numericValue = intPacket.Value;
            break;
        default:
            var boolPacket = new TelemetryPacket<bool>(req.SensorId, req.BoolValue ?? false, req.Category, ts);
            store.BoolHistories[req.SensorId].Insert(boolPacket);
            insertTicks = store.BoolHistories[req.SensorId].LastInsertTicks;
            numericValue = (req.BoolValue ?? false) ? 1 : 0;
            break;
    }

    store.RecordReceived(req.SensorId, numericValue, ts);
    store.RecordStored(insertTicks);

    var dto = new TelemetryReadingDto
    {
        SensorId = req.SensorId,
        Category = req.Category,
        NumericValue = numericValue,
        TimestampUtc = ts,
        IsAnomaly = IsAnomaly(req.Category, numericValue)
    };

    await hub.Clients.All.SendAsync("ReceiveTelemetry", dto);
    return Results.Ok(dto);
});

app.MapPost("/api/telemetry/batch", async (GenerateBatchRequest req, InMemoryStore store,
    Microsoft.AspNetCore.SignalR.IHubContext<TelemetryHub> hub) =>
{
    var sensorIds = req.SensorIds is { Count: > 0 }
        ? req.SensorIds
        : store.Sensors.Keys.Take(req.SensorCount).ToList();

    if (sensorIds.Count == 0)
        return Results.BadRequest(new { error = "No registered sensors available for batch simulation." });

    
    var jagged = BatchGenerator.GenerateRawEnvironmentalBatch(sensorIds.Count);
    store.RecordGenerated(jagged.Sum(row => row.Length));


    var packets = BatchGenerator.FlattenToPackets(jagged, sensorIds, SensorCategory.Environmental);
    store.RecordTransmitted(packets.Count);

    var dtos = new List<TelemetryReadingDto>();
    foreach (var packet in packets)
    {
        if (!store.FloatHistories.TryGetValue(packet.SensorId, out var history))
        {
            history = new TelemetryHistory<float>();
            store.FloatHistories[packet.SensorId] = history;
        }

        history.Insert(packet);
        store.RecordReceived(packet.SensorId, packet.Value, packet.TimestampUtc);
        store.RecordStored(history.LastInsertTicks);

        dtos.Add(new TelemetryReadingDto
        {
            SensorId = packet.SensorId,
            Category = SensorCategory.Environmental,
            NumericValue = packet.Value,
            TimestampUtc = packet.TimestampUtc,
            IsAnomaly = IsAnomaly(SensorCategory.Environmental, packet.Value)
        });
    }

    await hub.Clients.All.SendAsync("ReceiveBatch", dtos);
    return Results.Ok(new { batchSize = dtos.Count, jaggedRows = jagged.Length, dtos });
});



app.MapGet("/api/telemetry/history/{sensorId}", (string sensorId, DateTime? from, DateTime? to, InMemoryStore store) =>
{
    if (!store.FloatHistories.TryGetValue(sensorId, out var history))
        return Results.NotFound(new { error = $"No history for sensor '{sensorId}'." });

    var fromUtc = from ?? DateTime.UtcNow.AddHours(-24);
    var toUtc = to ?? DateTime.UtcNow;

    var results = history.SearchRange(fromUtc, toUtc);
    store.RecordSearch(history.LastSearchTicks);

    return Results.Ok(new
    {
        sensorId,
        count = results.Count,
        searchMicroseconds = history.LastSearchTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency,
        readings = results.Select(r => new { r.SensorId, r.Value, r.TimestampUtc })
    });
});



app.MapPost("/api/telemetry/performance-test/{sensorId}/{count:int}", (string sensorId, int count, InMemoryStore store) =>
{
    if (!store.FloatHistories.TryGetValue(sensorId, out var history))
        return Results.NotFound(new { error = $"No history for sensor '{sensorId}'." });

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var rnd = new Random();
    var baseTime = DateTime.UtcNow.AddDays(-1);
    for (int i = 0; i < count; i++)
    {
        var packet = new TelemetryPacket<float>(sensorId, (float)(20 + rnd.NextDouble() * 50), SensorCategory.Environmental, baseTime.AddSeconds(i));
        history.Insert(packet);
    }
    sw.Stop();

    var searchSw = System.Diagnostics.Stopwatch.StartNew();
    var mid = history.SearchRange(baseTime, baseTime.AddSeconds(count / 2.0));
    searchSw.Stop();

    return Results.Ok(new
    {
        insertedCount = count,
        totalInsertMs = sw.Elapsed.TotalMilliseconds,
        avgInsertMicroseconds = sw.Elapsed.TotalMilliseconds * 1000.0 / count,
        searchMs = searchSw.Elapsed.TotalMilliseconds,
        searchResultCount = mid.Count,
        totalRecordsNow = history.Count
    });
});



app.MapPost("/api/sensors/{sensorId}/attachments", async (string sensorId, HttpRequest request,
    InMemoryStore store, FileEncryptionService encryption) =>
{
    if (!store.Sensors.TryGetValue(sensorId, out var profile))
        return Results.NotFound(new { error = $"Unknown sensor '{sensorId}'." });

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data." });

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file provided." });

    var storedName = $"{sensorId}_{Guid.NewGuid():N}.enc";
    var storedPath = Path.Combine(uploadsDir, storedName);

    await using (var stream = file.OpenReadStream())
    {
        await encryption.EncryptToFileAsync(stream, storedPath);
    }

    var attachment = new AttachedFile
    {
        FileName = file.FileName,
        StoredPath = storedPath,
        SizeBytes = file.Length
    };
    profile.Attachments.Add(attachment);

    return Results.Ok(attachment);
});

app.Run();
