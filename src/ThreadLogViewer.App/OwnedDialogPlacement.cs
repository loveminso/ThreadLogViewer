using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ThreadLogViewer.App;

public readonly record struct DialogFrame(
    double Left, double Top, double Width, double Height, double MinWidth, double MinHeight);

/// <summary>
/// Centers a dialog on the owner's window rectangle and keeps it inside that monitor's work area.
/// WPF CenterOwner uses the owner's logical position, which stays stale after an external move.
/// </summary>
public static class OwnedDialogPlacement
{
    public static DialogFrame Fit(
        double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight,
        double workLeft, double workTop, double workWidth, double workHeight,
        double preferredWidth, double preferredHeight, double minWidth, double minHeight)
    {
        double widthLimit = double.IsFinite(workWidth) && workWidth > 1 ? workWidth : 1;
        double heightLimit = double.IsFinite(workHeight) && workHeight > 1 ? workHeight : 1;
        double width = Math.Clamp(Positive(preferredWidth, widthLimit), 1, widthLimit);
        double height = Math.Clamp(Positive(preferredHeight, heightLimit), 1, heightLimit);
        double minimumWidth = Math.Clamp(double.IsFinite(minWidth) && minWidth > 0 ? minWidth : 0, 0, width);
        double minimumHeight = Math.Clamp(double.IsFinite(minHeight) && minHeight > 0 ? minHeight : 0, 0, height);
        double left = ownerLeft + Math.Max(0, ownerWidth - width) / 2;
        double top = ownerTop + Math.Max(0, ownerHeight - height) / 2;
        double maxLeft = workLeft + Math.Max(0, widthLimit - width);
        double maxTop = workTop + Math.Max(0, heightLimit - height);
        if (left < workLeft) left = workLeft;
        if (left > maxLeft) left = maxLeft;
        if (top < workTop) top = workTop;
        if (top > maxTop) top = maxTop;
        return new(left, top, width, height, minimumWidth, minimumHeight);
    }

    public static void Bind(Window dialog, Window owner, double preferredWidth, double preferredHeight, double minWidth, double minHeight)
    {
        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Width = preferredWidth;
        dialog.Height = preferredHeight;
        dialog.MinWidth = Math.Min(Math.Max(0, minWidth), preferredWidth);
        dialog.MinHeight = Math.Min(Math.Max(0, minHeight), preferredHeight);
        Tracker.For(owner).Track(dialog, preferredWidth, preferredHeight, minWidth, minHeight);
    }

    private static double Positive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    private sealed class Tracker
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, Tracker> Owners = new();
        private readonly Window owner;
        private readonly Dictionary<Window, Spec> dialogs = new();
        private bool hooked, placing;

        private Tracker(Window owner)
        {
            this.owner = owner;
            owner.SourceInitialized += (_, _) => Hook();
            owner.LocationChanged += (_, _) => PlaceAll();
            owner.SizeChanged += (_, _) => PlaceAll();
            Hook();
        }

        public static Tracker For(Window owner) => Owners.GetValue(owner, window => new Tracker(window));

        public void Track(Window dialog, double preferredWidth, double preferredHeight, double minWidth, double minHeight)
        {
            dialogs[dialog] = new(preferredWidth, preferredHeight, minWidth, minHeight);
            dialog.SourceInitialized += (_, _) => Place(dialog);
            dialog.Loaded += (_, _) => Place(dialog);
            dialog.ContentRendered += (_, _) => Place(dialog);
            dialog.Closed += (_, _) => dialogs.Remove(dialog);
            Place(dialog);
        }

        private void Hook()
        {
            if (hooked) return;
            IntPtr handle = new WindowInteropHelper(owner).Handle;
            if (handle == IntPtr.Zero) return;
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source is null) return;
            source.AddHook(WndProc);
            hooked = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // External SetWindowPos does not raise LocationChanged. Follow the HWND instead.
            if (message == 0x0047) PlaceAll();
            return IntPtr.Zero;
        }

        private void PlaceAll()
        {
            if (placing) return;
            placing = true;
            try
            {
                foreach (var dialog in dialogs.Keys.ToArray())
                    Place(dialog);
            }
            finally { placing = false; }
        }

        private void Place(Window dialog)
        {
            if (!dialogs.TryGetValue(dialog, out var spec)) return;
            if (!TryRead(owner, out var ownerRect, out var workRect)) return;
            var frame = Fit(ownerRect.Left, ownerRect.Top, ownerRect.Width, ownerRect.Height,
                workRect.Left, workRect.Top, workRect.Width, workRect.Height,
                spec.PreferredWidth, spec.PreferredHeight, spec.MinWidth, spec.MinHeight);
            try
            {
                dialog.MinWidth = 0;
                dialog.MinHeight = 0;
                dialog.MaxWidth = Math.Max(frame.Width, workRect.Width);
                dialog.MaxHeight = Math.Max(frame.Height, workRect.Height);
                dialog.Width = frame.Width;
                dialog.Height = frame.Height;
                dialog.MinWidth = frame.MinWidth;
                dialog.MinHeight = frame.MinHeight;
                dialog.Left = frame.Left;
                dialog.Top = frame.Top;
            }
            catch (InvalidOperationException)
            {
                dialogs.Remove(dialog);
            }
        }

        private readonly record struct Spec(double PreferredWidth, double PreferredHeight, double MinWidth, double MinHeight);
        private readonly record struct DipRect(double Left, double Top, double Width, double Height);

        private static bool TryRead(Window owner, out DipRect ownerRect, out DipRect workRect)
        {
            ownerRect = default;
            workRect = default;
            IntPtr handle = new WindowInteropHelper(owner).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var pixels)) return false;
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not { } target) return false;
            Matrix toDip = target.TransformFromDevice;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return false;
            ownerRect = ToDip(toDip, pixels.Left, pixels.Top, pixels.Right, pixels.Bottom);
            workRect = ToDip(toDip, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            return workRect.Width > 1 && workRect.Height > 1;
        }

        private static DipRect ToDip(Matrix toDip, int left, int top, int right, int bottom)
        {
            Point origin = toDip.Transform(new Point(left, top));
            Point end = toDip.Transform(new Point(right, bottom));
            double x = Math.Min(origin.X, end.X);
            double y = Math.Min(origin.Y, end.Y);
            return new(x, y, Math.Max(1, Math.Abs(end.X - origin.X)), Math.Max(1, Math.Abs(end.Y - origin.Y)));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    }
}
