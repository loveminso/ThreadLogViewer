using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ThreadLogViewer.UiTests;

internal static class UiReviewCapture
{
    internal static string? TryRaiseResolution(int width, int height)
    {
        try
        {
            var mode = new DevMode { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(null, -1, ref mode))
                return "EnumDisplaySettings could not read the current mode. System DPI was left unchanged.";
            int beforeW = mode.dmPelsWidth;
            int beforeH = mode.dmPelsHeight;
            int screenW = GetSystemMetrics(0);
            int screenH = GetSystemMetrics(1);
            if (beforeW >= width && beforeH >= height)
                return $"Display is already {beforeW}x{beforeH} (system metrics {screenW}x{screenH}). System DPI was not changed.";
            mode.dmPelsWidth = width;
            mode.dmPelsHeight = height;
            mode.dmFields = 0x80000 | 0x100000;
            int result = ChangeDisplaySettings(ref mode, 0);
            int afterW = GetSystemMetrics(0);
            int afterH = GetSystemMetrics(1);
            return $"ChangeDisplaySettings({width}x{height}) from {beforeW}x{beforeH} returned {result}. System metrics are now {afterW}x{afterH}. System DPI was not changed.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "Display resolution was left unchanged: " + ex.GetType().Name + " " + ex.Message;
        }
    }

    internal static void Save(int processId, string path)
    {
        var windows = ProcessWindows(processId);
        if (windows.Count == 0)
            throw new InvalidOperationException("The viewer process has no visible window to capture.");
        int left = windows.Min(item => item.Left);
        int top = windows.Min(item => item.Top);
        int right = windows.Max(item => item.Right);
        int bottom = windows.Max(item => item.Bottom);
        int width = right - left;
        int height = bottom - top;
        if (width < 32 || height < 32 || width > 4000 || height > 2500)
            throw new InvalidOperationException($"Capture bounds {width}x{height} are not usable.");
        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.FromArgb(255, 20, 20, 20));
            foreach (var window in windows.OrderByDescending(item => (long)item.Width * item.Height))
            {
                using var shot = Print(window);
                if (shot is null)
                    continue;
                graphics.DrawImage(shot, window.Left - left, window.Top - top, shot.Width, shot.Height);
            }
        }
        canvas.Save(path, ImageFormat.Png);
    }

    internal static bool LooksBlank(string path)
    {
        using var source = new Bitmap(path);
        if (source.Width < 16 || source.Height < 16)
            return true;
        // GetPixel follows the bitmap orientation. A bottom-up stride would skip every row if sampled by hand.
        var colors = new HashSet<int>();
        int stepX = Math.Max(1, source.Width / 48);
        int stepY = Math.Max(1, source.Height / 48);
        for (int y = 0; y < source.Height && colors.Count < 5; y += stepY)
        {
            for (int x = 0; x < source.Width && colors.Count < 5; x += stepX)
            {
                var pixel = source.GetPixel(x, y);
                colors.Add(pixel.R | (pixel.G << 8) | (pixel.B << 16));
            }
        }
        return colors.Count < 5;
    }

    private static Bitmap? Print(WindowRect window)
    {
        try
        {
            var bitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            IntPtr dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(window.Handle, dc, 2) && !PrintWindow(window.Handle, dc, 0))
                {
                    graphics.ReleaseHdc(dc);
                    dc = IntPtr.Zero;
                    bitmap.Dispose();
                    return CopyScreen(window);
                }
            }
            finally
            {
                if (dc != IntPtr.Zero)
                    graphics.ReleaseHdc(dc);
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            return CopyScreen(window);
        }
    }

    private static Bitmap? CopyScreen(WindowRect window)
    {
        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        int left = Math.Max(0, window.Left);
        int top = Math.Max(0, window.Top);
        int right = Math.Min(screenW, window.Right);
        int bottom = Math.Min(screenH, window.Bottom);
        if (right - left < 16 || bottom - top < 16)
            return null;
        var bitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, left - window.Left, top - window.Top, new Size(right - left, bottom - top), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static List<WindowRect> ProcessWindows(int processId)
    {
        var found = new List<WindowRect>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != (uint)processId)
                return true;
            if (!GetWindowRect(hwnd, out var rect))
                return true;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 16 || height < 16)
                return true;
            found.Add(new WindowRect(hwnd, rect.Left, rect.Top, rect.Right, rect.Bottom));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private sealed record WindowRect(IntPtr Handle, int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DevMode devMode, int flags);

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    internal static void MoveWindow(IntPtr hwnd, int width, int height) =>
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0040 | 0x0004);

    internal static (int Width, int Height) WindowSize(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return (0, 0);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
}
