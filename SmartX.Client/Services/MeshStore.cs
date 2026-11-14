using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public class MeshStore
{
    private readonly MeshApiClient _api;

    public MeshStore(MeshApiClient api, StreamStore stream)
    {
        _api = api;
        stream.TopologyReceived += OnTopologyReceived;   // live pushes arrive over the one shared SignalR connection
    }

    public event Action? Changed;

    public MeshAnalysisDto? Analysis { get; private set; }
    public ConnectedDto? Connected { get; private set; }
    public RouteDto? Route { get; private set; }
    public HistoryQueryResultDto? History { get; private set; }
    public TreeBenchmarkDto? Benchmark { get; private set; }
    public string? Message { get; private set; }
    public string? SelectedMac { get; private set; }
    public bool Busy { get; private set; }

    // UI state kept here so it survives navigation
    public string SearchText { get; set; } = "";
    public bool ShowMst { get; set; } = true;
    public DateTime HistFrom { get; set; } = DateTime.Now.AddHours(-1);
    public DateTime HistTo { get; set; } = DateTime.Now.AddMinutes(1);
    public int HistLimit { get; set; } = 500;
    public int BenchCount { get; set; } = 50000;

    private void Notify() => Changed?.Invoke();

    private void OnTopologyReceived(MeshAnalysisDto analysis)
    {
        Analysis = analysis;
        _ = RefreshDerivedAsync();
        Notify();
    }

    public async Task RefreshAsync()
    {
        try { Analysis = await _api.GetTopologyAsync(); }
        catch (Exception ex) { Message = $"API unreachable: {ex.Message}"; }
        await RefreshDerivedAsync();
    }

    private async Task RefreshDerivedAsync()
    {
        try
        {
            Connected = await _api.GetConnectedAsync();
            if (SelectedMac is not null) Route = await _api.GetRouteAsync(SelectedMac);
        }
        catch (Exception ex) { Message = $"Refresh failed: {ex.Message}"; }
        Notify();
    }

    public async Task SelectAsync(string mac)
    {
        SelectedMac = mac;
        Route = null;
        History = null;
        try
        {
            Route = await _api.GetRouteAsync(mac);
            History = await _api.LatestAsync(mac, 100);
        }
        catch (Exception ex) { Message = $"Could not load node: {ex.Message}"; }
        Notify();
    }

    public async Task FindAsync()
    {
        var q = SearchText.Trim();
        if (q.Length == 0 || Analysis is null) return;

        var match = Analysis.Nodes.FirstOrDefault(n =>
            n.MacAddress.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            n.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            Message = $"No node matches '{q}'.";
            Notify();
            return;
        }
        Message = null;
        await SelectAsync(match.MacAddress);
    }

    public async Task FailAsync(string mac)
    {
        try
        {
            await _api.FailAsync(mac);
            Message = "Failure simulated. The mesh is re-routing around the dead node.";
        }
        catch (Exception ex) { Message = $"Failed: {ex.Message}"; }
        await RefreshAsync();
    }

    public async Task RestoreAsync(string mac)
    {
        try { await _api.RestoreAsync(mac); Message = "Node restored."; }
        catch (Exception ex) { Message = $"Failed: {ex.Message}"; }
        await RefreshAsync();
    }

    public async Task RestoreAllAsync()
    {
        try { await _api.RestoreAllAsync(); Message = "All simulated failures cleared."; }
        catch (Exception ex) { Message = $"Failed: {ex.Message}"; }
        await RefreshAsync();
    }

    public async Task QueryHistoryAsync()
    {
        if (SelectedMac is null) return;
        try
        {
            History = await _api.QueryHistoryAsync(SelectedMac, HistFrom.ToUniversalTime(), HistTo.ToUniversalTime(), HistLimit);
        }
        catch (Exception ex) { Message = $"History query failed: {ex.Message}"; }
        Notify();
    }

    public async Task LatestAsync()
    {
        if (SelectedMac is null) return;
        try { History = await _api.LatestAsync(SelectedMac, 100); }
        catch (Exception ex) { Message = $"History query failed: {ex.Message}"; }
        Notify();
    }

    public async Task RunBenchmarkAsync()
    {
        Busy = true;
        Notify();
        try { Benchmark = await _api.BenchmarkAsync(BenchCount); }
        catch (Exception ex) { Message = $"Benchmark failed: {ex.Message}"; }
        Busy = false;
        Notify();
    }
}