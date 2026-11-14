using System.Net.Http.Json;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public class MeshApiClient
{
    private readonly HttpClient _http;

    public MeshApiClient(string baseAddress) =>
        _http = new HttpClient { BaseAddress = new Uri(baseAddress) };

    public async Task<MeshAnalysisDto?> GetTopologyAsync() =>
        await _http.GetFromJsonAsync<MeshAnalysisDto>("/api/mesh/topology");

    public async Task<ConnectedDto?> GetConnectedAsync() =>
        await _http.GetFromJsonAsync<ConnectedDto>("/api/mesh/connected");

    public async Task<RouteDto?> GetRouteAsync(string mac) =>
        await _http.GetFromJsonAsync<RouteDto>($"/api/mesh/route/{Uri.EscapeDataString(mac)}");

    public async Task FailAsync(string mac) =>
        await _http.PostAsync($"/api/mesh/nodes/{Uri.EscapeDataString(mac)}/fail", null);

    public async Task RestoreAsync(string mac) =>
        await _http.PostAsync($"/api/mesh/nodes/{Uri.EscapeDataString(mac)}/restore", null);

    public async Task RestoreAllAsync() =>
        await _http.PostAsync("/api/mesh/restore-all", null);

    public async Task<HistoryQueryResultDto?> QueryHistoryAsync(string mac, DateTime fromUtc, DateTime toUtc, int limit)
    {
        var url = $"/api/mesh/history/{Uri.EscapeDataString(mac)}"
                  + $"?from={Uri.EscapeDataString(fromUtc.ToString("O"))}"
                  + $"&to={Uri.EscapeDataString(toUtc.ToString("O"))}&limit={limit}";
        return await _http.GetFromJsonAsync<HistoryQueryResultDto>(url);
    }

    public async Task<HistoryQueryResultDto?> LatestAsync(string mac, int count) =>
        await _http.GetFromJsonAsync<HistoryQueryResultDto>(
            $"/api/mesh/history/{Uri.EscapeDataString(mac)}/latest?count={count}");

    public async Task<TreeBenchmarkDto?> BenchmarkAsync(int count)
    {
        var response = await _http.PostAsync($"/api/mesh/benchmark?count={count}", null);
        return await response.Content.ReadFromJsonAsync<TreeBenchmarkDto>();
    }
}