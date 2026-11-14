using System.Net.Http.Json;
using System.Text.Json;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public class StreamApiClient
{
    private readonly HttpClient _http;
    public string BaseAddress { get; }

    public StreamApiClient(string baseAddress)
    {
        BaseAddress = baseAddress;
        _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public async Task<List<DeviceStateDto>> GetDevicesAsync() =>
        await _http.GetFromJsonAsync<List<DeviceStateDto>>("/api/devices") ?? new();

    public async Task<List<AlertDto>> GetAlertsAsync() =>
        await _http.GetFromJsonAsync<List<AlertDto>>("/api/alerts") ?? new();

    public async Task<List<SuggestedActionDto>> GetInsightsAsync() =>
        await _http.GetFromJsonAsync<List<SuggestedActionDto>>("/api/insights") ?? new();

    public async Task<StreamStatsDto?> GetStatsAsync() =>
        await _http.GetFromJsonAsync<StreamStatsDto>("/api/stream/stats");

    public async Task<CommandHistoryDto?> GetHistoryAsync() =>
        await _http.GetFromJsonAsync<CommandHistoryDto>("/api/commands/history");

    public async Task<List<TimelinePointDto>> GetTimelineAsync(string mac, int take) =>
        await _http.GetFromJsonAsync<List<TimelinePointDto>>(
            $"/api/stream/timeline/{Uri.EscapeDataString(mac)}?take={take}") ?? new();

    public async Task<CommandResultDto> PostCommandAsync(CommandRequest req) =>
        await ReadResultAsync(await _http.PostAsJsonAsync("/api/commands", req));

    public async Task<CommandResultDto> PostBulkAsync(BulkCommandRequest req) =>
        await ReadResultAsync(await _http.PostAsJsonAsync("/api/commands/bulk", req));

    public async Task<CommandResultDto> PostUndoAsync() =>
        await ReadResultAsync(await _http.PostAsync("/api/commands/undo", null));

    public async Task<bool> PostSearchEventAsync(string term)
    {
        var response = await _http.PostAsJsonAsync("/api/insights/search-event", new SearchEventRequest { Term = term });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("recorded", out var rec) && rec.GetBoolean();
    }

    public async Task<string> SeedAsync()
    {
        var response = await _http.PostAsync("/api/insights/seed", null);
        return response.IsSuccessStatusCode
            ? "Demo history loaded - learned patterns are now active."
            : "Seed failed: start the simulator first so devices exist.";
    }

    private static async Task<CommandResultDto> ReadResultAsync(HttpResponseMessage r)
    {
        try
        {
            return await r.Content.ReadFromJsonAsync<CommandResultDto>()
                   ?? new CommandResultDto { Message = $"HTTP {(int)r.StatusCode}" };
        }
        catch { return new CommandResultDto { Message = $"HTTP {(int)r.StatusCode}" }; }
    }
}