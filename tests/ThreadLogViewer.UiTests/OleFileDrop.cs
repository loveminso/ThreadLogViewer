using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ThreadLogViewer.UiTests;

/// <summary>
/// Cross-process file drop. A real Explorer drag is not available in CI.
/// This process is an OLE drag source: a WinForms <c>DataObject</c> carrying <c>FileDrop</c>
/// is given to <c>DoDragDrop</c>, and <c>SendInput</c> moves the pointer onto the viewer
/// window before the button is released. The viewer's existing <c>PreviewDrop</c> handler
/// reads that payload. The test does not inject into the viewer process.
/// </summary>
internal static class OleFileDrop
{
    private const uint MouseMove = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;
    private const int InputMouse = 0;

    public static void ReleasePointer()
    {
        try { SendMouse(LeftUp, 0, 0, absolute: false); }
        catch (EntryPointNotFoundException) { }
    }

    public static string Drop(IntPtr target, string file, Action<string> log)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("Drop payload is missing.", file);
        GetWindowRect(target, out RECT targetRect);
        int targetX = targetRect.Left + Math.Max(1, (targetRect.Right - targetRect.Left) / 2);
        int targetY = targetRect.Top + Math.Max(1, (targetRect.Bottom - targetRect.Top) / 2);
        using var source = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0),
            Size = new Size(24, 24),
            TopMost = true
        };
        source.Show();
        source.Refresh();
        Point start = source.PointToScreen(new Point(12, 12));
        int started = 0;
        source.GiveFeedback += (_, _) => Interlocked.Exchange(ref started, 1);
        var mover = new Thread(() => DrivePointer(start, targetX, targetY, () => Volatile.Read(ref started) == 1))
        {
            IsBackground = true,
            Name = "ThreadLogViewer drop pointer"
        };
        try
        {
            MoveAbsolute(start.X, start.Y);
            SendMouse(LeftDown, 0, 0, absolute: false);
            mover.Start();
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });
            var effect = source.DoDragDrop(data, DragDropEffects.Copy);
            log($"DoDragDrop effect={effect} started={Volatile.Read(ref started)}");
            return effect.ToString();
        }
        finally
        {
            if (!mover.Join(TimeSpan.FromSeconds(3)))
                ReleasePointer();
            ReleasePointer();
            source.Close();
        }
    }

    private static void DrivePointer(Point start, int targetX, int targetY, Func<bool> dragStarted)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (!dragStarted() && DateTime.UtcNow < until)
            Thread.Sleep(15);
        Thread.Sleep(120);
        const int steps = 12;
        for (int step = 1; step <= steps; step++)
        {
            int x = start.X + (targetX - start.X) * step / steps;
            int y = start.Y + (targetY - start.Y) * step / steps;
            MoveAbsolute(x, y);
            Thread.Sleep(35);
        }
        Thread.Sleep(80);
        SendMouse(LeftUp, 0, 0, absolute: false);
    }

    private static void MoveAbsolute(int x, int y)
    {
        int originX = GetSystemMetrics(76);
        int originY = GetSystemMetrics(77);
        int width = Math.Max(1, GetSystemMetrics(78));
        int height = Math.Max(1, GetSystemMetrics(79));
        int ax = (int)Math.Clamp((long)(x - originX) * 65535 / (width - 1), 0, 65535);
        int ay = (int)Math.Clamp((long)(y - originY) * 65535 / (height - 1), 0, 65535);
        SendMouse(MouseMove | Absolute | VirtualDesk, ax, ay, absolute: true);
    }

    private static void SendMouse(uint flags, int x, int y, bool absolute)
    {
        var input = new INPUT
        {
            type = InputMouse,
            mi = new MOUSEINPUT
            {
                dx = absolute ? x : 0,
                dy = absolute ? y : 0,
                dwFlags = flags
            }
        };
        int size = Marshal.SizeOf<INPUT>();
        if (size != 40)
            throw new InvalidOperationException("INPUT marshals to " + size + " bytes; x64 SendInput expects 40.");
        if (SendInput(1, new[] { input }, size) != 1)
            throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
