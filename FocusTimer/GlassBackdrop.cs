using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FocusTimer;

// Capture and GPU work stay on one MTA worker. Only the cropped final frame is
// handed to WPF; desktop pixels are never encoded, persisted or transmitted.
internal sealed class GlassBackdrop : IDisposable
{
    private volatile int targetFps = Preferences.DefaultGlassFps;
    private TimeSpan FrameInterval => TimeSpan.FromSeconds(1d / targetFps);
    private readonly Window window;
    private readonly Image image;
    private readonly Action changed;
    private readonly DispatcherTimer geometryTimer;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private volatile Geometry geometry = new(0, 0, 0, 0, 1, false);
    private bool disposed, failed, excluded, suspended;
    private nint hwnd;
    private int generation;
    private WriteableBitmap? bitmap;
    public bool IsAvailable { get; private set; }
    public string? Error { get; private set; }
    internal bool IsCapturing => cancellation is not null;
    internal bool IsExcluded => excluded;
    private sealed record Geometry(int X, int Y, int Width, int Height, float Dpi, bool Dark);

    public GlassBackdrop(Window window, Image image, Action changed)
    {
        this.window = window; this.image = image; this.changed = changed;
        window.IsVisibleChanged += VisibilityChanged;
        window.StateChanged += WindowChanged;
        window.LocationChanged += WindowChanged;
        window.SizeChanged += GeometryChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        geometryTimer = new DispatcherTimer(FrameInterval, DispatcherPriority.Background,
            (_, _) => Synchronize(), window.Dispatcher);
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => window.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (disposed) return;
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) { suspended = true; Stop(); }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) { suspended = false; failed = false; Synchronize(); }
    }));
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => window.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (disposed) return;
        if (e.Mode == PowerModes.Suspend) { suspended = true; Stop(); }
        else if (e.Mode == PowerModes.Resume) { suspended = false; failed = false; Synchronize(); }
    }));
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Synchronize();
    private void WindowChanged(object? sender, EventArgs e) => Synchronize();
    private void GeometryChanged(object sender, SizeChangedEventArgs e) => Synchronize();
    public void UpdateFrameRate(int framesPerSecond)
    {
        int value = Math.Clamp(framesPerSecond, Preferences.MinimumGlassFps, Preferences.MaximumGlassFps);
        if (targetFps == value) return;
        targetFps = value;
        geometryTimer.Interval = FrameInterval;
    }
    public void UpdateTheme(bool dark) { geometry = geometry with { Dark = dark }; Synchronize(); }
    private void Synchronize()
    {
        if (disposed) return;
        if (suspended || !window.IsVisible || window.WindowState == WindowState.Minimized) { Stop(); return; }
        hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return;
        geometry = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
            (float)VisualTreeHelper.GetDpi(window).DpiScaleX, geometry.Dark);
        if (failed || cancellation is not null || worker is { IsCompleted: false }) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { Fail("글래스는 Windows 11 이상에서 지원됩니다."); return; }
        if (!SetWindowDisplayAffinity(hwnd, 0x11)) { Fail("화면 캡처 제외를 적용할 수 없습니다."); return; }
        excluded = true;
        var source = cancellation = new CancellationTokenSource();
        var token = source.Token;
        int currentGeneration = ++generation;
        worker = Task.Run(() => { try { Run(token, currentGeneration); } finally { source.Dispose(); } });
    }
    private void Run(CancellationToken token, int currentGeneration)
    {
        nint renderer = 0;
        int retries = 0;
        using var pacer = new FramePacer(token);
        byte[] pixels = [];
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(GlassCreate(0, out renderer));
                    var sinceFrame = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested)
                    {
                        long frameStart = Stopwatch.GetTimestamp();
                        var g = geometry;
                        if (g.Width < 1 || g.Height < 1) { token.WaitHandle.WaitOne(FrameInterval); continue; }
                        int pixelCount = checked(g.Width * g.Height * 4);
                        if (pixels.Length != pixelCount) pixels = new byte[pixelCount];
                        int hr = GlassFrame(renderer, g.X, g.Y, g.Width, g.Height, g.Dpi, g.Dark ? 1 : 0, pixels, pixels.Length);
                        Marshal.ThrowExceptionForHR(hr);
                        if (hr == 0)
                        {
                            sinceFrame.Restart();
                            // Only one frame may be queued. Cancellation breaks the wait.
                            var operation = window.Dispatcher.InvokeAsync(() =>
                            {
                                if (disposed || token.IsCancellationRequested || generation != currentGeneration) return;
                                if (bitmap is null || bitmap.PixelWidth != g.Width || bitmap.PixelHeight != g.Height)
                                    bitmap = new WriteableBitmap(g.Width, g.Height, 96, 96, PixelFormats.Pbgra32, null);
                                bitmap.WritePixels(new Int32Rect(0, 0, g.Width, g.Height), pixels, g.Width * 4, 0);
                                image.Source = bitmap;
                                if (!IsAvailable) { IsAvailable = true; Error = null; changed(); }
                            }, DispatcherPriority.Render, token);
                            operation.Task.Wait(token);
                        }
                        else if (sinceFrame.Elapsed.TotalSeconds > 5) throw new TimeoutException();
                        pacer.Wait(FrameInterval - Stopwatch.GetElapsedTime(frameStart));
                    }
                    break;
                }
                catch (Exception ex) when (!token.IsCancellationRequested && retries++ == 0 && ex is not UnauthorizedAccessException)
                {
                    if (renderer != 0) { GlassDestroy(renderer); renderer = 0; }
                    token.WaitHandle.WaitOne(300);
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!disposed && generation == currentGeneration)
                        Fail(ex is UnauthorizedAccessException ? "화면 캡처 권한이 필요합니다." : "글래스를 사용할 수 없어 일반 투명 모드로 표시합니다.");
                }));
        }
        finally { if (renderer != 0) GlassDestroy(renderer); }
    }
    private void Fail(string message) { failed = true; Error = message; Stop(); changed(); }
    private void Stop()
    {
        bool wasAvailable = IsAvailable;
        try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
        cancellation = null; generation++;
        if (excluded && hwnd != 0) { SetWindowDisplayAffinity(hwnd, 0); excluded = false; }
        IsAvailable = false; image.Source = null; bitmap = null;
        if (wasAvailable) changed();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        geometryTimer.Stop();
        window.IsVisibleChanged -= VisibilityChanged;
        window.StateChanged -= WindowChanged;
        window.LocationChanged -= WindowChanged;
        window.SizeChanged -= GeometryChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        Stop();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(nint handle, uint affinity);
    [DllImport("FocusTimer.Glass.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int GlassCreate(int synthetic, out nint renderer);
    [DllImport("FocusTimer.Glass.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int GlassFrame(nint renderer, int x, int y, int width, int height, float dpi, int dark, [Out] byte[] pixels, int length);
    [DllImport("FocusTimer.Glass.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void GlassDestroy(nint renderer);
}
