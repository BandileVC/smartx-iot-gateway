using Microsoft.AspNetCore.SignalR;

namespace SmartX.Api.Hubs;

/// <summary>
/// Pushes ingested telemetry to connected dashboard clients in real time.
/// This is the transport for the "live anomaly pulse-feed" engagement strategy:
/// the server broadcasts every reading the instant it's ingested, tagged with
/// IsAnomaly, so the UI can pulse/highlight it immediately without polling.
/// </summary>
public class TelemetryHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}
