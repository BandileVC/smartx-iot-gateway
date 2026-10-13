using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public class PredictionEngine
{
    private const int WindowSize = 4;                  
    private const int MinSupport = 2;                  
    private const double MinConfidence = 0.5;
    private const double ForecastHorizonSeconds = 300.0;
    private static readonly TimeSpan WindowAge = TimeSpan.FromMinutes(5);

    private readonly DeviceRegistry _registry;
    private readonly TimelineStore _timeline;
    private readonly AlertTracker _alerts;

    public PredictionEngine(DeviceRegistry registry, TimelineStore timeline, AlertTracker alerts)
    {
        _registry = registry;
        _timeline = timeline;
        _alerts = alerts;
    }

    private sealed class Occurrence
    {
        public Occurrence(string key, DateTime at)
        {
            Key = key;
            At = at;
        }

        public string Key { get; }
        public DateTime At { get; }
        public HashSet<string> Credited { get; } = new HashSet<string>();   
    }

    private sealed record Candidate(string Source, string Target, int Count, int SourceCount);
    private sealed record Fit(double Slope, double R2);
    private sealed record Projection(double EtaSeconds, string Limit);

    
    private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();   
    private readonly Dictionary<string, int> _sourceCounts = new();                       
    private readonly Queue<Occurrence> _window = new();                                    
    private readonly object _gate = new();

    public void RecordSystemEvent(string key) => Record(key, false);
    public void RecordUserEvent(string key) => Record(key, true);

    public string? RecordSearch(string term)
    {
        var match = _registry.Query(term, null, false).FirstOrDefault();
        if (match is null) return null;
        RecordUserEvent("view|" + match.MacAddress);
        return match.Name;
    }

    private void Record(string key, bool learn)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            Expire(now);

            if (learn)
            {
                foreach (var prior in _window)
                {
                    if (prior.Key == key) continue;
                    if (!prior.Credited.Add(key)) continue;

                    if (!_transitions.TryGetValue(prior.Key, out var next))
                    {
                        next = new Dictionary<string, int>();
                        _transitions[prior.Key] = next;
                    }

                    next.TryGetValue(key, out var existing);
                    next[key] = existing + 1;
                }
            }

            _sourceCounts.TryGetValue(key, out var occurrences);
            _sourceCounts[key] = occurrences + 1;

            _window.Enqueue(new Occurrence(key, now));
            while (_window.Count > WindowSize) _window.Dequeue();
        }
    }

    private void Expire(DateTime now)
    {
        while (_window.Count > 0 && now - _window.Peek().At > WindowAge)
            _window.Dequeue();
    }

    
    public void SeedDemoHistory(string alertKey, string viewKey, string commandKey, int repetitions)
    {
        lock (_gate) { _window.Clear(); }

        for (var i = 0; i < repetitions; i++)
        {
            RecordSystemEvent(alertKey);
            RecordUserEvent(viewKey);
            RecordUserEvent(commandKey);

            if (i < repetitions - 1)
            {
                lock (_gate) { _window.Clear(); }
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    public List<SuggestedActionDto> BuildInsights()
    {
        var devices = _registry.All();
        var byMac = new Dictionary<string, DeviceStateDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in devices) byMac[item.MacAddress] = item;

        var ranked = new PriorityQueue<SuggestedActionDto, double>();   
        var claimed = new HashSet<string>();

        
        
        var context = new HashSet<string>();
        foreach (var alert in _alerts.Snapshot())
            context.Add("alert|" + alert.MacAddress + "|" + alert.Type);

        var candidates = new List<Candidate>();
        lock (_gate)
        {
            Expire(DateTime.UtcNow);

            foreach (var o in _window)
            {
                if (!o.Key.StartsWith("alert|", StringComparison.Ordinal))
                    context.Add(o.Key);
            }

            foreach (var source in context)
            {
                if (!_transitions.TryGetValue(source, out var next)) continue;
                if (!_sourceCounts.TryGetValue(source, out var seen) || seen <= 0) continue;

                foreach (var pair in next)
                {
                    var confidenceValue = (double)pair.Value / seen;
                    if (pair.Value >= MinSupport && confidenceValue >= MinConfidence)
                        candidates.Add(new Candidate(source, pair.Key, pair.Value, seen));
                }
            }
        }

        var orderedCandidates = candidates
            .OrderByDescending(x => (double)x.Count / x.SourceCount)
            .ToList();

        foreach (var cand in orderedCandidates)
        {
            var parts = cand.Target.Split('|');
            if (parts.Length < 2) continue;
            if (!byMac.TryGetValue(parts[1], out var targetDevice)) continue;

            var confidence = Math.Min(1.0, (double)cand.Count / cand.SourceCount);
            var trigger = Describe(cand.Source, byMac);
            var learnedScore = 60.0 + confidence * 30.0 + Math.Min(cand.Count, 10);
            var reason = "Followed \"" + trigger + "\" in " + cand.Count + " of " + cand.SourceCount + " past occurrences.";

            if (parts[0] == "cmd" && parts.Length == 3)
            {
                var wantOn = parts[2] == "ON";
                if (targetDevice.ActuatorOn == wantOn) continue;   

                Offer(ranked, claimed, Make(
                    "learned:" + cand.Target, InsightKind.LearnedPattern,
                    "Turn " + parts[2] + " " + targetDevice.Name, reason,
                    confidence, learnedScore, targetDevice.MacAddress, parts[2]));
            }
            else if (parts[0] == "view")
            {
                Offer(ranked, claimed, Make(
                    "learned:" + cand.Target, InsightKind.LearnedPattern,
                    "Inspect " + targetDevice.Name, reason,
                    confidence, learnedScore - 10.0, targetDevice.MacAddress, null));
            }
        }

        
        foreach (var dev in devices)
        {
            if (dev.Category == SensorCategory.Actuator) continue;
            if (dev.Status == DeviceStatus.Critical || dev.Status == DeviceStatus.Offline) continue;

            var pts = _timeline.Recent(dev.MacAddress, 12);
            if (pts.Count < 6) continue;

            var fit = Regress(pts);
            var perMinute = fit.Slope * 60.0;
            if (fit.R2 < 0.6) continue;                        
            if (Math.Abs(perMinute) < 1.0) continue;           

            var projection = Project(dev.Category, pts[pts.Count - 1].Value, fit.Slope);
            if (projection is null) continue;

            var eta = projection.EtaSeconds;
            if (eta > ForecastHorizonSeconds) continue;

            var direction = perMinute > 0 ? "Rising" : "Falling";
            var title = dev.Name + " projected to hit " + projection.Limit + " in ~" + eta.ToString("F0") + "s";
            var why = direction + " " + Math.Abs(perMinute).ToString("F1") + " units/min over the last "
                      + pts.Count + " readings (fit R2 " + fit.R2.ToString("F2") + ").";
            var forecastScore = 70.0 + (1.0 - eta / ForecastHorizonSeconds) * 25.0;

            Offer(ranked, claimed, Make(
                "forecast:" + dev.MacAddress, InsightKind.PredictedBreach,
                title, why, fit.R2, forecastScore, dev.MacAddress, null));
        }

        
        foreach (var d in devices)
        {
            if (d.Status == DeviceStatus.Online) continue;

            string attentionTitle;
            double attentionScore;
            if (d.Status == DeviceStatus.Critical)
            {
                attentionTitle = "Check " + d.Name + " now";
                attentionScore = 95.0;
            }
            else if (d.Status == DeviceStatus.Offline)
            {
                attentionTitle = "Reconnect " + d.Name;
                attentionScore = 75.0;
            }
            else
            {
                attentionTitle = "Review " + d.Name;
                attentionScore = 55.0;
            }

            var lastValue = d.LatestValue.HasValue ? d.LatestValue.Value.ToString("F1") : "n/a";
            Offer(ranked, claimed, Make(
                "attention:" + d.MacAddress, InsightKind.NeedsAttention,
                attentionTitle, "Status " + d.Status + ", last value " + lastValue + ".",
                1.0, attentionScore, d.MacAddress, null));
        }

        
        var result = new List<SuggestedActionDto>();
        while (result.Count < 8 && ranked.TryDequeue(out var top, out _))
            result.Add(top);
        return result;
    }

    private static void Offer(PriorityQueue<SuggestedActionDto, double> ranked, HashSet<string> claimed, SuggestedActionDto s)
    {
        if (claimed.Add(s.Id)) ranked.Enqueue(s, -s.Score);
    }

    private static SuggestedActionDto Make(string id, InsightKind kind, string title, string reason,
        double confidence, double score, string mac, string? command)
    {
        return new SuggestedActionDto
        {
            Id = id,
            Kind = kind,
            Title = title,
            Reason = reason,
            Confidence = confidence,
            Score = score,
            TargetMac = mac,
            SuggestedCommand = command
        };
    }

    private static Fit Regress(List<(DateTime At, double Value)> pts)
    {
        var n = pts.Count;
        var t0 = pts[0].At.Ticks;
        var xs = new double[n];
        var ys = new double[n];

        for (var i = 0; i < n; i++)
        {
            xs[i] = (pts[i].At.Ticks - t0) / (double)TimeSpan.TicksPerSecond;
            ys[i] = pts[i].Value;
        }

        var mx = xs.Average();
        var my = ys.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - mx;
            var dy = ys[i] - my;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        if (sxx < 1e-9 || syy < 1e-9) return new Fit(0, 0);
        return new Fit(sxy / sxx, (sxy * sxy) / (sxx * syy));
    }

    private static Projection? Project(SensorCategory category, double latest, double slope)
    {
        if (category == SensorCategory.Environmental)
        {
            if (slope < 0 && latest > ReadingClassifier.MoistureFloor)
            {
                var eta = (latest - ReadingClassifier.MoistureFloor) / (-slope);
                return new Projection(eta, "the " + ReadingClassifier.MoistureFloor + "% floor");
            }
            if (slope > 0 && latest < ReadingClassifier.MoistureCeiling)
            {
                var eta = (ReadingClassifier.MoistureCeiling - latest) / slope;
                return new Projection(eta, "the " + ReadingClassifier.MoistureCeiling + "% ceiling");
            }
        }
        else if (category == SensorCategory.PowerConsumption)
        {
            if (slope > 0 && latest < ReadingClassifier.PowerCeiling)
            {
                var eta = (ReadingClassifier.PowerCeiling - latest) / slope;
                return new Projection(eta, "the " + ReadingClassifier.PowerCeiling + " W limit");
            }
        }
        return null;
    }

    private static string Describe(string key, Dictionary<string, DeviceStateDto> byMac)
    {
        var p = key.Split('|');
        var name = "?";
        if (p.Length > 1)
            name = byMac.TryGetValue(p[1], out var d) ? d.Name : p[1];

        if (p[0] == "alert" && p.Length > 2) return name + ": " + FriendlyAlert(p[2]);
        if (p[0] == "view") return "you searched " + name;
        if (p[0] == "cmd" && p.Length > 2) return "you turned " + p[2] + " " + name;
        return key;
    }

    private static string FriendlyAlert(string type)
    {
        if (type == "MOISTURE_CRITICAL") return "moisture critically low";
        if (type == "POWER_SPIKE") return "power spike";
        if (type == "OUT_OF_RANGE") return "out-of-range reading";
        if (type == "DISCONNECTED") return "device disconnected";
        return type;
    }
}