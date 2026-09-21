namespace FocusTimer.Core;

public enum TimerState { Ready, Running, Paused, Completed }

public sealed class Countdown(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private DateTimeOffset deadline;
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(25);
    public TimerState State { get; private set; }
    public event Action? Completed;

    public void Configure(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero || duration > new TimeSpan(99, 59, 59))
            throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = Remaining = duration;
        State = TimerState.Ready;
    }

    public void Start()
    {
        if (State == TimerState.Running || Remaining <= TimeSpan.Zero) return;
        deadline = clock.GetUtcNow() + Remaining;
        State = TimerState.Running;
    }

    public void Pause()
    {
        if (State != TimerState.Running) return;
        Tick();
        if (State == TimerState.Running) State = TimerState.Paused;
    }

    public void Reset() => Configure(Duration);

    public void StartUntil(DateTimeOffset target)
    {
        var now = clock.GetUtcNow();
        Configure(target > now ? target - now : TimeSpan.Zero);
        deadline = target;
        if (Remaining > TimeSpan.Zero) State = TimerState.Running;
    }

    public void Tick()
    {
        if (State != TimerState.Running) return;
        Remaining = deadline - clock.GetUtcNow();
        if (Remaining > TimeSpan.Zero) return;
        Remaining = TimeSpan.Zero;
        State = TimerState.Completed;
        Completed?.Invoke();
    }

    public static DateTimeOffset NextClockTime(DateTimeOffset now, int minute)
    {
        if (minute is not (0 or 30)) throw new ArgumentOutOfRangeException(nameof(minute));
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, minute, 0, now.Offset);
        return target > now ? target : target.AddHours(1);
    }

    public static string Format(TimeSpan remaining)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}
