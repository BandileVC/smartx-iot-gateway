using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using SmartX.Shared.Models;


var apiBase = Environment.GetEnvironmentVariable("SMARTX_API") ?? "http://localhost:5050";
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "run";
using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
var rnd = new Random();

var fleet = new[]
{
    new SimDevice("AA:BB:CC:00:00:01", "Moisture Sensor A", SensorCategory.Environmental, "Facility A/Zone 1/Sub-Zone B", 48),
    new SimDevice("AA:BB:CC:00:00:02", "Moisture Sensor B", SensorCategory.Environmental, "Facility A/Zone 1/Sub-Zone C", 42),
    new SimDevice("AA:BB:CC:00:00:03", "Smart Meter 1", SensorCategory.PowerConsumption, "Facility A/Zone 2/Sub-Zone A", 1800),
    new SimDevice("AA:BB:CC:00:00:04", "Smart Meter 2", SensorCategory.PowerConsumption, "Facility B/Zone 1/Sub-Zone A", 2200),
    new SimDevice("AA:BB:CC:00:00:05", "Pump A", SensorCategory.Actuator, "Facility A/Zone 1/Sub-Zone C", 0),
    new SimDevice("AA:BB:CC:00:00:06", "Valve 1", SensorCategory.Actuator, "Facility A/Zone 1/Sub-Zone B", 0),
    new SimDevice("AA:BB:CC:00:00:07", "Smart Relay 1", SensorCategory.Actuator, "Facility B/Zone 1/Sub-Zone A", 0),
};
var moistureB = fleet.First(f => f.Name == "Moisture Sensor B");
var meter1 = fleet.First(f => f.Name == "Smart Meter 1");
var pump = fleet.First(f => f.Name == "Pump A");

Console.WriteLine($"Smart-X simulator -> {apiBase} (mode: {mode})");
await RegisterAllAsync();

switch (mode)
{
    case "critical":
        
        await SendAsync(meter1, 4800);
        await SendAsync(moistureB, 9);
        Console.WriteLine("Sent critical power spike (4800 W) and critical moisture (9%).");
        break;

    case "flood":
        
        var count = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 800;
        var sensors = fleet.Where(f => f.Category != SensorCategory.Actuator).ToArray();
        var sw = Stopwatch.StartNew();
        var batch = new List<Task>();
        for (var i = 0; i < count; i++)
        {
            var d = sensors[i % sensors.Length];
            var v = d.Category == SensorCategory.Environmental ? 45 + rnd.NextDouble() * 10 : 1500 + rnd.NextDouble() * 500;
            batch.Add(SendAsync(d, v));
            if (batch.Count == 50) { await Task.WhenAll(batch); batch.Clear(); }
        }
        await Task.WhenAll(batch);
        Console.WriteLine($"Flooded {count} normal packets in {sw.ElapsedMilliseconds} ms. Sending CRITICAL now...");
        await SendAsync(moistureB, 8);
        Console.WriteLine("Critical alert sent. Check the dashboard: it should be processed ahead of the backlog.");
        break;

    default:
        Console.WriteLine("Streaming 1 reading/second per device. Ctrl+C to stop (watchdog will flag disconnects after ~20s).");
        while (true)
        {
            var pumpOn = await DesiredOnAsync(pump);
            foreach (var d in fleet)
            {
                double v;
                switch (d.Category)
                {
                    case SensorCategory.Environmental when d.Name == "Moisture Sensor B":
                         
                        d.Value = Math.Clamp(d.Value + (pumpOn ? 2.5 : -1.2) + (rnd.NextDouble() - 0.5) * 0.6, 5, 60);
                        v = d.Value;
                        break;
                    case SensorCategory.Environmental:
                        v = d.Baseline + (rnd.NextDouble() - 0.5) * 6;
                        break;
                    case SensorCategory.PowerConsumption:
                        v = rnd.NextDouble() < 0.03 ? 4300 + rnd.Next(0, 400) : d.Baseline + (rnd.NextDouble() - 0.5) * 300;
                        break;
                    default:
                        v = await DesiredOnAsync(d) ? 1 : 0;
                        break;
                }
                await SendAsync(d, v);
            }
            await Task.Delay(1000);
        }
}


async Task RegisterAllAsync()
{
    foreach (var d in fleet)
    {
        try
        {
            await http.PostAsJsonAsync("/api/devices/register", new DeviceRegisterRequest
            { MacAddress = d.Mac, Name = d.Name, Category = d.Category, Location = d.Location });
        }
        catch (Exception ex) { Console.WriteLine($"Register {d.Name} failed: {ex.Message}"); }
    }
}

async Task SendAsync(SimDevice d, double value)
{
    try
    {
        var r = await http.PostAsJsonAsync("/api/stream/ingest", new DeviceTelemetryRequest
        {
            MacAddress = d.Mac, Name = d.Name, Category = d.Category, Value = Math.Round(value, 2),
            TimestampUtc = DateTime.UtcNow
        });
        if (!r.IsSuccessStatusCode) Console.WriteLine($"{d.Name}: HTTP {(int)r.StatusCode}");
    }
    catch (Exception ex) { Console.WriteLine($"{d.Name}: {ex.Message}"); }
}

async Task<bool> DesiredOnAsync(SimDevice d)
{
    try
    {
        var json = await http.GetFromJsonAsync<JsonElement>($"/api/devices/{Uri.EscapeDataString(d.Mac)}/command");
        return json.GetProperty("desiredOn").GetBoolean();
    }
    catch { return false; }
}

record SimDevice(string Mac, string Name, SensorCategory Category, string Location, double Baseline)
{
    public double Value { get; set; } = Baseline;
}