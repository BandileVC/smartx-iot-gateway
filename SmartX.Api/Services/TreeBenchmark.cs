using System.Diagnostics;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public static class TreeBenchmark
{
    public static TreeBenchmarkDto Run(int count)
    {
        TreeBenchmarkDto? result = null;
        var worker = new Thread(() => { result = RunCore(count); }, 64 * 1024 * 1024);
        worker.Start();
        worker.Join();
        return result!;
    }

    private static double Micros(Stopwatch sw, int runs) =>
        sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency / runs;

    private static TreeBenchmarkDto RunCore(int count)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dto = new TreeBenchmarkDto { Count = count };

        
        var from = baseTime.AddSeconds((int)(count * 0.45));
        var to = baseTime.AddSeconds((int)(count * 0.46));

        
        var avl = new AvlTree<DateTime, double>();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) avl.Insert(baseTime.AddSeconds(i), i);
        sw.Stop();
        dto.AvlInsertMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

        const int avlRuns = 200;
        var matches = 0;
        sw.Restart();
        for (var r = 0; r < avlRuns; r++) matches = avl.Range(from, to).Count;
        sw.Stop();
        dto.AvlRangeMicros = Math.Round(Micros(sw, avlRuns), 1);
        dto.WindowMatches = matches;
        dto.AvlHeight = avl.Height;
        dto.MaxPossibleAvlHeight = (int)Math.Ceiling(1.44 * Math.Log2(count + 2));
        dto.AvlRotations = avl.RotationCount;
        dto.AvlBalanced = avl.VerifyBalanced();

        
        var list = new List<KeyValuePair<DateTime, double>>(count);
        sw.Restart();
        for (var i = 0; i < count; i++) list.Add(new KeyValuePair<DateTime, double>(baseTime.AddSeconds(i), i));
        sw.Stop();
        dto.ListInsertMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

        const int listRuns = 20;
        sw.Restart();
        for (var r = 0; r < listRuns; r++)
        {
            var hits = new List<KeyValuePair<DateTime, double>>();
            foreach (var kv in list)
            {
                if (kv.Key >= from && kv.Key <= to) hits.Add(kv);
            }
        }
        sw.Stop();
        dto.ListScanMicros = Math.Round(Micros(sw, listRuns), 1);
        dto.RangeSpeedupVsList = dto.AvlRangeMicros > 0
            ? Math.Round(dto.ListScanMicros / dto.AvlRangeMicros, 1)
            : 0;

        
        var bstCount = Math.Min(count, 10000);
        dto.BstCount = bstCount;

        var avlSmall = new AvlTree<DateTime, double>();
        sw.Restart();
        for (var i = 0; i < bstCount; i++) avlSmall.Insert(baseTime.AddSeconds(i), i);
        sw.Stop();
        dto.AvlInsertMsAtBstCount = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

        var bst = new TelemetryHistory<float>();
        sw.Restart();
        for (var i = 0; i < bstCount; i++)
            bst.Insert(new TelemetryPacket<float>("bench", i, SensorCategory.Environmental, baseTime.AddSeconds(i)));
        sw.Stop();
        dto.BstInsertMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

        var bFrom = baseTime.AddSeconds((int)(bstCount * 0.45));
        var bTo = baseTime.AddSeconds((int)(bstCount * 0.46));
        sw.Restart();
        for (var r = 0; r < avlRuns; r++) bst.SearchRange(bFrom, bTo);
        sw.Stop();
        dto.BstRangeMicros = Math.Round(Micros(sw, avlRuns), 1);

        return dto;
    }
}