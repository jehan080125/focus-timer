using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace FocusTimer;

internal static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    public static void ClampToScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || window.WindowState != WindowState.Normal || !GetWindowRect(handle, out var bounds)) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
        int x = Math.Clamp(bounds.Left, monitor.Work.Left, Math.Max(monitor.Work.Left, monitor.Work.Right - (bounds.Right - bounds.Left)));
        int y = Math.Clamp(bounds.Top, monitor.Work.Top, Math.Max(monitor.Work.Top, monitor.Work.Bottom - (bounds.Bottom - bounds.Top)));
        if (x != bounds.Left || y != bounds.Top) SetWindowPos(handle, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    public static void NoActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | (nint)0x08000000);
    }

    // DragMove enters Windows' interactive move loop, which triggers edge/corner
    // Snap. Move directly in screen pixels instead; native resize borders remain.
    public static bool BeginFreeDrag(Window window, Action? finished = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || window.WindowState != WindowState.Normal ||
            Mouse.LeftButton != MouseButtonState.Pressed ||
            !GetCursorPos(out var start) || !GetWindowRect(handle, out var bounds) ||
            !window.CaptureMouse()) return false;

        bool ended = false;
        void End()
        {
            if (ended) return;
            ended = true;
            window.PreviewMouseMove -= Move;
            window.PreviewMouseLeftButtonUp -= Release;
            window.LostMouseCapture -= LostCapture;
            window.Deactivated -= Deactivated;
            window.Closed -= Deactivated;
            window.PreviewKeyDown -= KeyDown;
            if (Mouse.Captured == window) window.ReleaseMouseCapture();
            finished?.Invoke();
        }
        void Move(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { End(); return; }
            if (GetCursorPos(out var current))
                SetWindowPos(handle, 0, bounds.Left + current.X - start.X,
                    bounds.Top + current.Y - start.Y, 0, 0,
                    0x0001 | 0x0004 | 0x0010); // No resize, Z-order or activation change.
            e.Handled = true;
        }
        void Release(object sender, MouseButtonEventArgs e) { e.Handled = true; End(); }
        void LostCapture(object sender, MouseEventArgs e) => End();
        void Deactivated(object? sender, EventArgs e) => End();
        void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; End(); }
        }
        window.PreviewMouseMove += Move;
        window.PreviewMouseLeftButtonUp += Release;
        window.LostMouseCapture += LostCapture;
        window.Deactivated += Deactivated;
        window.Closed += Deactivated;
        window.PreviewKeyDown += KeyDown;
        return true;
    }
}
