namespace ThreadLogViewer.Tests;

public static class MemoryGrowthCheck
{
    public const long ManagedHeapFloorBytes = 32L * 1024 * 1024;
    public const long WorkingSetFloorBytes = 64L * 1024 * 1024;
    public const int MinimumRefreshSamples = 3;
    public const string SuspectedLeakUnconfirmed = "누수 의심, 원인 미확인";
    public const string NoWarningVerdict = "지속 증가 경고 없음. 누수 없음으로 결론내지 않는다.";
    public const string ManagedHeapMarginRule = "max(32 MiB, after-open post-GC managed heap / 50)";
    public const string WorkingSetMarginRule = "max(64 MiB, after-open post-GC working set / 25)";

    public static long ManagedHeapMargin(long afterOpenManagedHeap) =>
        Math.Max(ManagedHeapFloorBytes, afterOpenManagedHeap / 50);

    public static long WorkingSetMargin(long afterOpenWorkingSet) =>
        Math.Max(WorkingSetFloorBytes, afterOpenWorkingSet / 25);

    // Refresh samples only. A series keeps rising when every later sample is strictly greater
    // than the previous one and the last sample exceeds the first by more than the margin.
    // Fewer than three samples cannot meet this rule. A warning is not a conclusion that a leak exists.
    public static bool KeepsRising(IReadOnlyList<long> refreshSamples, long margin)
    {
        if (refreshSamples.Count < MinimumRefreshSamples) return false;
        if (refreshSamples[^1] - refreshSamples[0] <= margin) return false;
        for (int i = 1; i < refreshSamples.Count; i++)
        {
            if (refreshSamples[i] <= refreshSamples[i - 1]) return false;
        }
        return true;
    }

    public static string? WarningText(bool managedHeapKeepsRising, bool workingSetKeepsRising, int mib)
    {
        if (!managedHeapKeepsRising && !workingSetKeepsRising) return null;
        var metrics = new List<string>(2);
        if (managedHeapKeepsRising) metrics.Add("post-GC managed heap");
        if (workingSetKeepsRising) metrics.Add("working set");
        return $"{mib} MiB {string.Join(" and ", metrics)}: {SuspectedLeakUnconfirmed}";
    }

    public static string Verdict(bool managedHeapKeepsRising, bool workingSetKeepsRising, int mib) =>
        WarningText(managedHeapKeepsRising, workingSetKeepsRising, mib) ?? NoWarningVerdict;
}
