using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;


public class DeviceWatchdog(DeviceRegistry registry, AlertTracker alerts, PredictionEngine engine,
    IHubContext<TelemetryHub> hub, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(config.GetValue("Pipeline:DisconnectTimeoutSeconds", 20));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                foreach (var stale in registry.StaleDevices(timeout))
                {
                    var changed = registry.MarkOffline(stale.MacAddress);
                    var alert = new AlertDto
                    {
                        Key = AlertTracker.KeyFor("DISCONNECTED", stale.MacAddress), Type = "DISCONNECTED",
                        MacAddress = stale.MacAddress, DeviceName = stale.Name, Severity = AlertSeverity.Warning,
                        Message = $"No telemetry for over {timeout.TotalSeconds:F0}s.", RaisedUtc = DateTime.UtcNow
                    };
                    if (alerts.TryRaise(alert))
                    {
                        engine.RecordSystemEvent($"alert|{stale.MacAddress}|DISCONNECTED");
                        await hub.Clients.All.SendAsync("ReceiveAlert", alert, ct);
                    }
                    if (changed)
                    {
                        stale.Status = DeviceStatus.Offline;
                        await hub.Clients.All.SendAsync("ReceiveDeviceUpdate", stale, ct);
                    }
                }
                await hub.Clients.All.SendAsync("ReceiveInsights", engine.BuildInsights(), ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}