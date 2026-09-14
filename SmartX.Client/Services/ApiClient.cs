using System.Net.Http.Json;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

/// <summary>
/// Thin async wrapper around the SmartX ingestion API. Kept separate from the
/// injected HttpClient so Blazor components stay declarative and testable.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    public string BaseAddress { get; }

    public ApiClient(string baseAddress)
    {
        BaseAddress = baseAddress;
        _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public async Task<ApiStatusDto?> GetStatusAsync() =>
        await _http.GetFromJsonAsync<ApiStatusDto>("/api/status");

    public async Task<List<SensorProfile>?> GetSensorsAsync() =>
        await _http.GetFromJsonAsync<List<SensorProfile>>("/api/sensors");

    public async Task<(bool Success, SensorProfile? Sensor, string? Error)> RegisterSensorAsync(RegisterSensorRequest req)
    {
        var response = await _http.PostAsJsonAsync("/api/sensors/register", req);
        if (response.IsSuccessStatusCode)
            return (true, await response.Content.ReadFromJsonAsync<SensorProfile>(), null);

        var error = await response.Content.ReadAsStringAsync();
        return (false, null, error);
    }

    public async Task<TelemetryReadingDto?> IngestAsync(IngestTelemetryRequest req)
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry/ingest", req);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TelemetryReadingDto>() : null;
    }

    public async Task<HttpResponseMessage> GenerateBatchAsync(GenerateBatchRequest req) =>
        await _http.PostAsJsonAsync("/api/telemetry/batch", req);

    public async Task<HttpResponseMessage> SearchHistoryAsync(string sensorId, DateTime from, DateTime to) =>
        await _http.GetAsync($"/api/telemetry/history/{sensorId}?from={from:O}&to={to:O}");

    public async Task<HttpResponseMessage> RunPerformanceTestAsync(string sensorId, int count) =>
        await _http.PostAsync($"/api/telemetry/performance-test/{sensorId}/{count}", null);

    public async Task<ValidationResult?> ValidateDeploymentAsync(List<string> path)
    {
        var response = await _http.PostAsJsonAsync("/api/deployment/validate", new DeploymentValidationRequest { Path = path });
        return await response.Content.ReadFromJsonAsync<ValidationResult>();
    }

    public async Task<HttpResponseMessage> UploadAttachmentAsync(string sensorId, MultipartFormDataContent content) =>
        await _http.PostAsync($"/api/sensors/{sensorId}/attachments", content);
}
