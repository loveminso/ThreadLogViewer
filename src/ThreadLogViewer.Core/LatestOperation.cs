namespace ThreadLogViewer.Core;

// A generation check is required even after cancellation: a completed older task may be queued
// on the dispatcher already. Only the newest generation may publish its result.
public sealed class LatestOperation : IDisposable
{
    private CancellationTokenSource? active;
    private long generation;
    public (long Version, CancellationToken Token) Begin()
    {
        active?.Cancel();
        active?.Dispose();
        active = new();
        return (++generation, active.Token);
    }
    public bool IsCurrent(long version) => version == generation;
    public void Cancel() => active?.Cancel();
    public void Dispose() { active?.Cancel(); active?.Dispose(); active = null; generation++; }
}
