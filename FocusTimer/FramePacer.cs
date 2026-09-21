using System.Runtime.InteropServices;

namespace FocusTimer;

// A cancellable, one-shot high-resolution timer avoids whole-millisecond frame
// rounding and busy waiting. It exists only while glass capture is running.
internal sealed class FramePacer : IDisposable
{
    private readonly CancellationToken cancellation;
    private readonly nint timer;
    private readonly nint[] handles;
    public FramePacer(CancellationToken cancellation)
    {
        this.cancellation = cancellation;
        timer = CreateWaitableTimerExW(0, 0, 0x2, 0x00100002);
        handles = [cancellation.WaitHandle.SafeWaitHandle.DangerousGetHandle(), timer];
    }
    public void Wait(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero || cancellation.IsCancellationRequested) return;
        long due = -Math.Max(1, remaining.Ticks);
        if (timer != 0 && SetWaitableTimer(timer, ref due, 0, 0, 0, false))
        {
            if (WaitForMultipleObjects(2, handles, false, uint.MaxValue) != uint.MaxValue) return;
        }
        cancellation.WaitHandle.WaitOne((int)Math.Ceiling(remaining.TotalMilliseconds));
    }
    public void Dispose() { if (timer != 0) CloseHandle(timer); }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint callback, nint context, bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForMultipleObjects(uint count, [In] nint[] handles, bool waitAll, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
