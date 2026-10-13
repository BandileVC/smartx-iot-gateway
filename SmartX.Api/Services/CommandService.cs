using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class CommandService(DeviceRegistry registry, PredictionEngine engine, IHubContext<TelemetryHub> hub)
{
    private const int MaxAudit = 200;
    private readonly Stack<CommandRecordDto> _undo = new();     
    private readonly List<CommandRecordDto> _audit = new();     
    private readonly object _gate = new();
    private long _nextId;

    public int UndoDepth { get { lock (_gate) return _undo.Count; } }

    public async Task<CommandResultDto> IssueAsync(CommandRequest req)
    {
        if (!registry.TryGet(req.MacAddress, out var device))
            return Fail($"Unknown device '{req.MacAddress}'.");
        if (device.Category != SensorCategory.Actuator)
            return Fail($"{device.Name} is a sensor - only actuators accept override commands.");

        var action = (req.Action ?? "").Trim().ToUpperInvariant();
        bool? target = action switch { "ON" => true, "OFF" => false, "TOGGLE" => !device.ActuatorOn, _ => null };
        if (target is null) return Fail("Action must be ON, OFF or TOGGLE.");

        var word = target.Value ? "ON" : "OFF";
        var change = registry.SetActuator(device.MacAddress, target.Value);
        if (change is null || !change.Changed)
            return Fail($"{device.Name} is already {word}.");

        var record = Push($"{device.Name} -> {word}", word, req.Source, false, false, new() { ToChange(change) });
        engine.RecordUserEvent($"cmd|{device.MacAddress}|{word}");   
        await BroadcastAsync(record, new[] { change.Device });
        return new CommandResultDto { Success = true, Message = $"{device.Name} turned {word}.", Record = record };
    }

    public async Task<CommandResultDto> BulkAsync(BulkCommandRequest req)
    {
        var action = (req.Action ?? "").Trim().ToUpperInvariant();
        if (action is not ("ON" or "OFF")) return Fail("Bulk action must be ON or OFF.");
        var on = action == "ON";

        var changes = new List<ActuatorChange>();
        foreach (var d in registry.Actuators(req.NameContains))
        {
            var c = registry.SetActuator(d.MacAddress, on);
            if (c is { Changed: true }) changes.Add(c);
        }
        if (changes.Count == 0) return Fail("No devices needed to change.");

        var scope = string.IsNullOrWhiteSpace(req.NameContains) ? "all actuators" : $"all '{req.NameContains}' devices";
        var record = Push($"Bulk {action}: {scope} ({changes.Count})", action, "Operator", true, false,
                          changes.Select(ToChange).ToList());
        await BroadcastAsync(record, changes.Select(c => c.Device));
        return new CommandResultDto { Success = true, Message = record.Description, Record = record };
    }

    public async Task<CommandResultDto> UndoAsync()
    {
        CommandRecordDto? last;
        lock (_gate)
        {
            if (!_undo.TryPop(out last)) return Fail("Nothing to undo");
        }

        var reverted = new List<ActuatorChange>();
        foreach (var ch in last.Changes)
        {
            var r = registry.SetActuator(ch.MacAddress, ch.PreviousOn);
            if (r is { Changed: true }) reverted.Add(r);
        }

        CommandRecordDto record;
        lock (_gate)
        {
            record = new CommandRecordDto
            {
                Id = ++_nextId, IssuedUtc = DateTime.UtcNow, Action = "UNDO", Source = "Operator",
                IsBulk = last.IsBulk, IsUndo = true, Description = $"UNDO: {last.Description}",
                Changes = reverted.Select(ToChange).ToList()
            };
            AddAudit(record);
        }
        await BroadcastAsync(record, reverted.Select(c => c.Device));
        return new CommandResultDto { Success = true, Message = record.Description, Record = record };
    }

    public CommandHistoryDto Snapshot()
    {
        lock (_gate)
            return new CommandHistoryDto
            {
                UndoDepth = _undo.Count,
                NextUndo = _undo.TryPeek(out var top) ? top : null,
                Audit = Enumerable.Reverse(_audit).Take(50).ToList()
            };
    }

    private CommandRecordDto Push(string description, string action, string source, bool bulk, bool undo,
        List<StateChangeDto> changes)
    {
        lock (_gate)
        {
            var record = new CommandRecordDto
            {
                Id = ++_nextId, IssuedUtc = DateTime.UtcNow, Description = description, Action = action,
                Source = source, IsBulk = bulk, IsUndo = undo, Changes = changes
            };
            _undo.Push(record);
            AddAudit(record);
            return record;
        }
    }

    private void AddAudit(CommandRecordDto r)
    {
        _audit.Add(r);
        if (_audit.Count > MaxAudit) _audit.RemoveAt(0);
    }

    private static StateChangeDto ToChange(ActuatorChange c) => new()
    {
        MacAddress = c.Device.MacAddress, DeviceName = c.Device.Name,
        PreviousOn = c.Previous, NewOn = c.Device.ActuatorOn
    };

    private async Task BroadcastAsync(CommandRecordDto record, IEnumerable<DeviceStateDto> devices)
    {
        foreach (var d in devices) await hub.Clients.All.SendAsync("ReceiveDeviceUpdate", d);
        await hub.Clients.All.SendAsync("ReceiveCommand", record);
        await hub.Clients.All.SendAsync("ReceiveInsights", engine.BuildInsights());
    }

    private static CommandResultDto Fail(string message) => new() { Success = false, Message = message };
}