using Microsoft.AspNetCore.SignalR.Client;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;


public class StreamStore : IAsyncDisposable
{
    private readonly StreamApiClient _api;
    private HubConnection? _hub;
    private CancellationTokenSource? _cts;
    private bool _started, _notifyScheduled;

    public StreamStore(StreamApiClient api) => _api = api;

    public event Action? Changed;

    public bool HubConnected { get; private set; }
    public string? LastError { get; private set; }
    public string? Message { get; private set; }

    public Dictionary<string, DeviceStateDto> Devices { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StreamReadingDto> Feed { get; } = new(); 
    public List<AlertDto> Alerts { get; } = new();
    public List<SuggestedActionDto> Insights { get; private set; } = new();
    public StreamStatsDto? Stats { get; private set; }
    public CommandHistoryDto? History { get; private set; }
    public List<TimelinePointDto> Timeline { get; private set; } = new();

    
    public string FilterQuery { get; set; } = "";
    public string FilterCategory { get; set; } = "";
    public bool AlertsOnly { get; set; }
    public string? SelectedMac { get; set; }

    public SensorCategory? CategoryFilter =>
        Enum.TryParse<SensorCategory>(FilterCategory, out var c) ? c : null;

    public async Task StartAsync()
    {
        if (_started) return;               
        _started = true;
        _cts = new CancellationTokenSource();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_api.BaseAddress}/hubs/telemetry")
            .WithAutomaticReconnect()
            .Build();

        _hub.On<StreamReadingDto>("ReceiveStreamReading", OnReading);
        _hub.On<AlertDto>("ReceiveAlert", a =>
        {
            Alerts.RemoveAll(x => x.Key == a.Key);
            Alerts.Insert(0, a);
            Notify();
        });
        _hub.On<string>("ReceiveAlertCleared", key => { Alerts.RemoveAll(x => x.Key == key); Notify(); });
        _hub.On<DeviceStateDto>("ReceiveDeviceUpdate", d => { Devices[d.MacAddress] = d; Notify(); });
        _hub.On<CommandRecordDto>("ReceiveCommand", cmd => { _ = RefreshHistoryAsync(); });
        _hub.On<List<SuggestedActionDto>>("ReceiveInsights", list => { Insights = list; Notify(); });

        _hub.Reconnecting += _ => { HubConnected = false; Notify(); return Task.CompletedTask; };
        _hub.Reconnected += async _ => { HubConnected = true; await RefreshAllAsync(); };
        _hub.Closed += _ => { HubConnected = false; Notify(); return Task.CompletedTask; };

        await RefreshAllAsync();
        try { await _hub.StartAsync(); HubConnected = true; } catch { HubConnected = false; }
        Notify();

        _ = PollLoopAsync(_cts.Token);
    }

    
    private void OnReading(StreamReadingDto r)
    {
        if (r.Device is not null) Devices[r.MacAddress] = r.Device;

        Feed.Insert(0, r);
        if (Feed.Count > 150) Feed.RemoveRange(150, Feed.Count - 150);

        if (string.Equals(r.MacAddress, SelectedMac, StringComparison.OrdinalIgnoreCase))
        {
            Timeline.Add(new TimelinePointDto { TimestampUtc = r.TimestampUtc, Value = r.Value });
            if (Timeline.Count > 150) Timeline.RemoveAt(0);
        }
        Notify();
    }

    
    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_hub is { State: HubConnectionState.Disconnected })
                {
                    await _hub.StartAsync(ct);
                    HubConnected = true;
                    await RefreshAllAsync();
                }
                Stats = await _api.GetStatsAsync();
                LastError = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LastError = ex.Message; HubConnected = false; }

            Notify();
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var devices = await _api.GetDevicesAsync();
            Devices.Clear();
            foreach (var d in devices) Devices[d.MacAddress] = d;

            Alerts.Clear();
            Alerts.AddRange(await _api.GetAlertsAsync());
            Insights = await _api.GetInsightsAsync();
            History = await _api.GetHistoryAsync();
            Stats = await _api.GetStatsAsync();
            LastError = null;
        }
        catch (Exception ex) { LastError = ex.Message; }
        Notify();
    }

    private async Task RefreshHistoryAsync()
    {
        try { History = await _api.GetHistoryAsync(); } catch { }
        Notify();
    }

    
    public async Task SendCommandAsync(string mac, string action, string source = "Operator")
    {
        try
        {
            var result = await _api.PostCommandAsync(new CommandRequest { MacAddress = mac, Action = action, Source = source });
            Message = result.Message;
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { Message = $"Command failed: {ex.Message}"; }
        Notify();
    }

    public async Task BulkAsync(string action, string? nameContains)
    {
        try
        {
            var result = await _api.PostBulkAsync(new BulkCommandRequest { Action = action, NameContains = nameContains });
            Message = result.Message;
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { Message = $"Bulk command failed: {ex.Message}"; }
        Notify();
    }

    public async Task UndoAsync()
    {
        try
        {
            var result = await _api.PostUndoAsync();
            Message = result.Message;
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { Message = $"Undo failed: {ex.Message}"; }
        Notify();
    }

    
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(FilterQuery)) return;
        try
        {
            var recorded = await _api.PostSearchEventAsync(FilterQuery);
            Message = recorded ? "Search logged for the predictive engine." : "No matching device.";
            Insights = await _api.GetInsightsAsync();
        }
        catch (Exception ex) { Message = $"Search failed: {ex.Message}"; }
        Notify();
    }

    public async Task SelectAsync(string mac)
    {
        SelectedMac = mac;
        try { Timeline = await _api.GetTimelineAsync(mac, 150); } catch { Timeline = new(); }
        Notify();
    }

    public async Task SeedDemoAsync()
    {
        try { Message = await _api.SeedAsync(); Insights = await _api.GetInsightsAsync(); }
        catch (Exception ex) { Message = $"Seed failed: {ex.Message}"; }
        Notify();
    }

    
    private void Notify()
    {
        if (_notifyScheduled) return;
        _notifyScheduled = true;
        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        await Task.Delay(100);
        _notifyScheduled = false;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_hub is not null) await _hub.DisposeAsync();
    }
}