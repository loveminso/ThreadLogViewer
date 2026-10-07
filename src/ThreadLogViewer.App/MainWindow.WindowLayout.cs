using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ThreadLogViewer.App;

public sealed record WindowLayoutLimits(double Width, double Height, double MinWidth, double MinHeight, double MaxWidth, double MaxHeight);

/// <summary>Physical work-area pixels become DIPs before choosing window bounds.</summary>
public static class WindowLayoutPolicy
{
    public static WindowLayoutLimits Calculate(double workWidthPixels, double workHeightPixels, double dpiX = 96,
        double dpiY = 96, double requestedWidth = 1360, double requestedHeight = 860)
    {
        if (!double.IsFinite(workWidthPixels) || workWidthPixels <= 0) throw new ArgumentOutOfRangeException(nameof(workWidthPixels));
        if (!double.IsFinite(workHeightPixels) || workHeightPixels <= 0) throw new ArgumentOutOfRangeException(nameof(workHeightPixels));
        if (!double.IsFinite(dpiX) || dpiX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiX));
        if (!double.IsFinite(dpiY) || dpiY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiY));
        double width = workWidthPixels * 96 / dpiX, height = workHeightPixels * 96 / dpiY;
        double minWidth = Math.Min(720, width), minHeight = Math.Min(420, height);
        double Initial(double requested, double minimum, double maximum, double fallback) =>
            Math.Clamp(double.IsFinite(requested) && requested > 0 ? requested : fallback, minimum, Math.Max(minimum, maximum - 16));
        return new(Initial(requestedWidth, minWidth, width, 1360), Initial(requestedHeight, minHeight, height, 860),
            minWidth, minHeight, width, height);
    }
}

public partial class MainWindow
{
    private bool windowLayoutReady, updatingResponsiveLayout, windowBoundsQueued, monitorBoundsInitialized;
    private HwndSource? windowLayoutSource;

    private void InitializeWindowLayout()
    {
        windowLayoutReady = true;
        SourceInitialized += WindowLayout_SourceInitialized;
        WorkspaceGrid.LayoutUpdated += WindowLayout_Updated;
        // SystemParameters already returns DIPs; the 96-DPI identity conversion is deliberate.
        var workArea = SystemParameters.WorkArea;
        ApplyWindowBounds(WindowLayoutPolicy.Calculate(workArea.Width, workArea.Height));
    }

    private void WindowLayout_SourceInitialized(object? sender, EventArgs e)
    {
        windowLayoutSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        windowLayoutSource?.AddHook(WindowLayout_Message);
        UpdateMonitorWindowBounds();
    }

    private IntPtr WindowLayout_Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Let WPF process the suggested DPI rectangle before reading the new window DPI.
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0232) QueueMonitorWindowBounds();
        return IntPtr.Zero;
    }

    private void QueueMonitorWindowBounds()
    {
        if (!windowLayoutReady || windowBoundsQueued) return;
        windowBoundsQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            windowBoundsQueued = false;
            if (windowLayoutReady) UpdateMonitorWindowBounds();
        }));
    }

    private void UpdateMonitorWindowBounds()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = new WindowMonitorInfo { Size = Marshal.SizeOf<WindowMonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
        uint dpi = GetDpiForWindow(handle);
        if (dpi == 0) dpi = 96;
        var area = monitor.Work;
        var limits = WindowLayoutPolicy.Calculate(area.Right - area.Left, area.Bottom - area.Top, dpi, dpi, Width, Height);
        if (monitorBoundsInitialized)
            limits = limits with { Width = Math.Clamp(Width, limits.MinWidth, limits.MaxWidth), Height = Math.Clamp(Height, limits.MinHeight, limits.MaxHeight) };
        ApplyWindowBounds(limits);
        monitorBoundsInitialized = true;
        if (WindowState == WindowState.Normal && GetWindowRect(handle, out var bounds))
        {
            int left = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - (bounds.Right - bounds.Left)));
            int top = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - (bounds.Bottom - bounds.Top)));
            if (left != bounds.Left || top != bounds.Top) _ = SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x0015);
        }
        UpdateLayoutLimits();
    }

    private void ApplyWindowBounds(WindowLayoutLimits limits)
    {
        // Lower a prior monitor's minimum before applying a smaller maximum.
        MinWidth = 0; MinHeight = 0;
        MaxWidth = limits.MaxWidth; MaxHeight = limits.MaxHeight;
        MinWidth = limits.MinWidth; MinHeight = limits.MinHeight;
        if (WindowState == WindowState.Normal) { Width = limits.Width; Height = limits.Height; }
    }

    private void WindowLayout_Updated(object? sender, EventArgs e) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        if (!windowLayoutReady || updatingResponsiveLayout || WorkspaceGrid.ActualHeight <= 0) return;
        updatingResponsiveLayout = true;
        try
        {
            // Keep the virtualized list outside all configuration ScrollViewers.
            double sidebarHeight = Math.Max(0, LimitResultsViewportToSlot(FilterPanel.ActualHeight, FilterPanel));
            double remaining = Math.Max(0, sidebarHeight - ThreadScopeControls.DesiredSize.Height);
            double threadMinimum = Math.Min(96, remaining);
            double settingsMaximum = Math.Max(0, remaining - threadMinimum);
            if (SidebarSettingsScroll.MaxHeight != settingsMaximum) SidebarSettingsScroll.MaxHeight = settingsMaximum;
            if (ThreadList.MinHeight != threadMinimum) ThreadList.MinHeight = threadMinimum;

            double sidebarMaximum = Math.Min(600, Math.Max(0, WorkspaceGrid.ActualWidth - 364));
            double sidebarMinimum = Math.Min(275, sidebarMaximum);
            if (ThreadColumn.MinWidth > sidebarMaximum) ThreadColumn.MinWidth = sidebarMaximum;
            if (ThreadColumn.MaxWidth != sidebarMaximum) ThreadColumn.MaxWidth = sidebarMaximum;
            if (ThreadColumn.MinWidth != sidebarMinimum) ThreadColumn.MinWidth = sidebarMinimum;

            double viewport = LimitResultsViewportToSlot(LogContentGrid.ActualHeight, LogContentGrid);
            if (LogContentGrid.Parent is FrameworkElement parent) viewport = LimitResultsViewportToSlot(viewport, parent);
            double resultHeader = ResultsPanel.Visibility == Visibility.Visible ? ResultsHeaderDesiredHeight() + (resultsCollapsed ? 0 : 6) : 0;
            double topMaximum = Math.Max(0, viewport - 120 - resultHeader);
            if (LogControlsScroll.MaxHeight != topMaximum) LogControlsScroll.MaxHeight = topMaximum;
        }
        finally { updatingResponsiveLayout = false; }
    }

    private void DisposeWindowLayout()
    {
        windowLayoutReady = false;
        SourceInitialized -= WindowLayout_SourceInitialized;
        WorkspaceGrid.LayoutUpdated -= WindowLayout_Updated;
        windowLayoutSource?.RemoveHook(WindowLayout_Message);
        windowLayoutSource = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMonitorInfo { public int Size; public WindowRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref WindowMonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
