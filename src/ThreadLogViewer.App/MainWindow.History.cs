using System.Windows;
using System.Windows.Input;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private sealed record NavigationPoint(PositionAnchor Position, bool Context, int? ContextLine);
    private sealed class NavigationHistory
    {
        public List<NavigationPoint> Back { get; } = [];
        public List<NavigationPoint> Forward { get; } = [];
    }
    private const int MaximumNavigationHistory = 100;
    private int navigationDepth;
    private bool historyReplaying;

    private NavigationPoint? CaptureNavigationPoint()
    {
        var position = CapturePosition();
        return position is null ? null : new(position, contextActive, contextActive ? margin.ContextLineIndex : null);
    }

    // Nested context/search navigation is recorded as one user action.
    private NavigationTransaction BeginNavigation() => new(this);
    private sealed class NavigationTransaction : IDisposable
    {
        private readonly MainWindow owner;
        private readonly LogSession? session;
        private readonly LogData? source;
        private readonly NavigationPoint? before;
        private readonly bool outermost;
        public NavigationTransaction(MainWindow owner)
        {
            this.owner = owner; session = owner.activeSession; source = owner.data;
            outermost = owner.navigationDepth++ == 0;
            if (outermost && !owner.historyReplaying) before = owner.CaptureNavigationPoint();
        }
        public void Dispose()
        {
            owner.navigationDepth--;
            if (!outermost || owner.historyReplaying || session != owner.activeSession || source != owner.data || before is null) return;
            var after = owner.CaptureNavigationPoint();
            if (after is null || SamePlace(before, after)) return;
            Push(session!.History.Back, before);
            session.History.Forward.Clear();
            owner.UpdateMenus();
        }
    }
    private static bool SamePlace(NavigationPoint first, NavigationPoint second) =>
        first.Position.SourceLine == second.Position.SourceLine && first.Position.Column == second.Position.Column &&
        first.Context == second.Context && first.ContextLine == second.ContextLine;
    private static void Push(List<NavigationPoint> stack, NavigationPoint point)
    {
        if (stack.Count > 0 && SamePlace(stack[^1], point)) return;
        stack.Add(point);
        if (stack.Count > MaximumNavigationHistory) stack.RemoveAt(0);
    }
    private async void BackNavigation_Click(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(false);
    private async void ForwardNavigation_Click(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(true);
    private async Task NavigateHistoryAsync(bool forward)
    {
        if (busy || historyReplaying || activeSession is not { } session || data is null) return;
        var stack = forward ? session.History.Forward : session.History.Back;
        if (stack.Count == 0) return;
        var source = data;
        var target = stack[^1];
        var current = CaptureNavigationPoint();
        historyReplaying = true;
        try
        {
            if (target.Context)
            {
                if (!await ShowContextAsync(target.ContextLine ?? target.Position.SourceLine)) return;
            }
            else if (contextActive)
            {
                await FilterAsync(returnAnchor: target.Position);
                if (contextActive) return; // cancellation leaves the current view and stacks untouched
            }
            if (source != data || session != activeSession) return;
            restoringPosition = true;
            try { RestorePosition(target.Position); }
            finally { restoringPosition = false; }
            stack.RemoveAt(stack.Count - 1);
            if (current is not null) Push(forward ? session.History.Back : session.History.Forward, current);
            Editor.Focus(); UpdatePosition();
            OperationStatus.Text = $"{(forward ? "앞으로" : "뒤로")} 이동 · 원본 {(CurrentSourceLine() ?? target.Position.SourceLine) + 1:N0}줄" +
                (positionMovedToNearest ? " · 필터로 숨겨진 이전 위치에 가까운 줄을 표시합니다." : "");
        }
        finally { historyReplaying = false; UpdateMenus(); }
    }

    private void RemapNavigationHistory(LogSession session, SourceLineRemap map)
    {
        var mapped = new NavigationHistory();
        foreach (var (oldStack, newStack) in new[] { (session.History.Back, mapped.Back), (session.History.Forward, mapped.Forward) })
            foreach (var point in oldStack)
            {
                if (map.Map(point.Position.SourceLine) is not { } line) continue;
                int? center = point.ContextLine is { } context ? map.Map(context) : null;
                if (point.Context && center is null) continue;
                var position = point.Position with { SourceLine = line, TopSourceLine = map.Map(point.Position.TopSourceLine) ?? line };
                Push(newStack, point with { Position = position, ContextLine = center });
            }
        session.History = mapped;
    }

    private bool TryHandleRecoveryShortcut(KeyEventArgs e, ModifierKeys modifiers)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.T) RestoreClosedSession_Click(this, new());
        else if (modifiers == ModifierKeys.None && key == Key.F5) RefreshSession_Click(this, new());
        else if (modifiers == ModifierKeys.Alt && key == Key.Left) BackNavigation_Click(this, new());
        else if (modifiers == ModifierKeys.Alt && key == Key.Right) ForwardNavigation_Click(this, new());
        else return false;
        e.Handled = true;
        return true;
    }
}
