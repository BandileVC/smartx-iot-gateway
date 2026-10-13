using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class TelemetryProcessor(
    TelemetryPipeline pipeline, DeviceRegistry registry, TimelineStore timeline, AlertTracker alerts,
    PredictionEngine engine, StreamMetrics metrics, IHubContext<TelemetryHub> hub,
    IConfiguration config, ILogger<TelemetryProcessor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
         
        var delayMs = config.GetValue("Pipeline:ProcessingDelayMs", 5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var packet = await pipeline.DequeueAsync(stoppingToken);   
                if (packet is null) continue;
                await ProcessAsync(packet);
                if (delayMs > 0) await Task.Delay(delayMs, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Failed to process telemetry packet"); }
        }
    }

    private async Task ProcessAsync(QueuedPacket p)
    {
        var latencyMs = (DateTime.UtcNow - p.EnqueuedUtc).TotalMilliseconds;
        metrics.RecordProcessed(p.IsPriority, latencyMs);

        var device = registry.ApplyReading(p.Mac, p.Value, p.Severity);   
        if (device is null) return;
        timeline.Add(p.Mac, p.TimestampUtc, p.Value);                      

        
        var dcKey = AlertTracker.KeyFor("DISCONNECTED", p.Mac);
        if (alerts.Clear(dcKey)) await hub.Clients.All.SendAsync("ReceiveAlertCleared", dcKey);

        if (p.AlertType is not null)
        {
            var alert = new AlertDto
            {
                Key = AlertTracker.KeyFor(p.AlertType, p.Mac), Type = p.AlertType, MacAddress = p.Mac,
                DeviceName = device.Name, Severity = p.Severity ?? AlertSeverity.Warning,
                Message = p.AlertMessage ?? p.AlertType, RaisedUtc = DateTime.UtcNow
            };
            if (alerts.TryRaise(alert))                                    
            {
                engine.RecordSystemEvent($"alert|{p.Mac}|{p.AlertType}");
                await hub.Clients.All.SendAsync("ReceiveAlert", alert);
            }
        }
        else
        {
            foreach (var type in ReadingClassifier.ReadingAlertTypes)
            {
                var key = AlertTracker.KeyFor(type, p.Mac);
                if (alerts.Clear(key)) await hub.Clients.All.SendAsync("ReceiveAlertCleared", key);
            }
        }

        await hub.Clients.All.SendAsync("ReceiveStreamReading", new StreamReadingDto
        {
            Sequence = p.Sequence, MacAddress = p.Mac, Name = device.Name, Category = p.Category,
            Value = p.Value, TimestampUtc = p.TimestampUtc, Severity = p.Severity,
            Lane = p.IsPriority ? "Priority" : "Standard", QueueLatencyMs = Math.Round(latencyMs, 2),
            Device = device
        });

        if (p.Severity is not null)
            await hub.Clients.All.SendAsync("ReceiveInsights", engine.BuildInsights());
    }
}