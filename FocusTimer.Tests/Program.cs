using FocusTimer.Core;

int passed = 0;
void Test(string name, Action run) { run(); Console.WriteLine($"PASS {name}"); passed++; }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
var start = new DateTimeOffset(2026, 9, 20, 14, 40, 0, TimeSpan.FromHours(9));

Test("14:10 -> next :30 at 14:30", () => Equal(start.AddMinutes(-10), Countdown.NextClockTime(start.AddMinutes(-30), 30)));
Test("14:40 -> next :30 at 15:30 (never 15:00)", () => Equal(start.AddMinutes(50), Countdown.NextClockTime(start, 30)));
Test("Next hour discards seconds and fractions", () => Equal(start.AddMinutes(20), Countdown.NextClockTime(start.AddSeconds(59.9), 0)));
Test("Exact :30 advances a full hour", () => Equal(start.AddMinutes(50), Countdown.NextClockTime(start.AddMinutes(-10), 30)));
Test("Exact hour advances a full hour", () => Equal(start.AddMinutes(20), Countdown.NextClockTime(start.AddMinutes(-40), 0)));
Test("Midnight rollover targets 00:30", () => Equal(new DateTimeOffset(2026, 9, 21, 0, 30, 0, start.Offset), Countdown.NextClockTime(new DateTimeOffset(2026, 9, 20, 23, 59, 59, start.Offset), 30)));
Test("Just before :30 selects the imminent half hour", () => Equal(start.AddMinutes(-10), Countdown.NextClockTime(start.AddMinutes(-10).AddMilliseconds(-1), 30)));
Test("Just after :30 skips the upcoming whole hour", () => Equal(start.AddMinutes(50), Countdown.NextClockTime(start.AddMinutes(-10).AddMilliseconds(1), 30)));
Test("Display uses ceiling and supports 99 hours", () => { Equal("00:00:01", Countdown.Format(TimeSpan.FromMilliseconds(1))); Equal("99:59:59", Countdown.Format(new TimeSpan(99, 59, 59))); Equal("00:00:00", Countdown.Format(TimeSpan.FromSeconds(-1))); });
Test("Pause preserves fractional remaining time and resume ignores paused interval", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock);
    timer.Configure(TimeSpan.FromSeconds(10)); timer.Start(); clock.Advance(2.25); timer.Pause();
    Equal(TimerState.Paused, timer.State); Equal(7.75, timer.Remaining.TotalSeconds);
    clock.Advance(100); timer.Tick(); Equal(7.75, timer.Remaining.TotalSeconds);
    timer.Start(); clock.Advance(.75); timer.Tick(); Equal(7d, timer.Remaining.TotalSeconds);
});
Test("Sleep or delayed UI catches up and completes only once", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock); int completions = 0;
    timer.Completed += () => completions++;
    timer.Configure(TimeSpan.FromSeconds(10)); timer.Start(); clock.Advance(500); timer.Tick(); timer.Tick(); timer.Pause();
    Equal(TimerState.Completed, timer.State); Equal(TimeSpan.Zero, timer.Remaining); Equal(1, completions);
});
Test("Reset restores original duration and stops", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock);
    timer.Configure(TimeSpan.FromSeconds(40)); timer.Start(); clock.Advance(20); timer.Reset();
    Equal(TimerState.Ready, timer.State); Equal(40d, timer.Remaining.TotalSeconds);
});
Test("Quick start replaces running deadline", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock); timer.Start();
    clock.Advance(10); timer.StartUntil(start.AddSeconds(30)); clock.Advance(10); timer.Tick();
    Equal(10d, timer.Remaining.TotalSeconds); Equal(20d, timer.Duration.TotalSeconds);
});
Test("Zero cannot start; invalid durations rejected", () =>
{
    var timer = new Countdown(); timer.Configure(TimeSpan.Zero); timer.Start(); Equal(TimerState.Ready, timer.State);
    bool caught = false; try { timer.Configure(TimeSpan.FromHours(100)); } catch (ArgumentOutOfRangeException) { caught = true; } Equal(true, caught);
});
Test("Pause at deadline delivers completion instead of a paused zero", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock); timer.Configure(TimeSpan.FromSeconds(1)); timer.Start(); clock.Advance(1); timer.Pause(); Equal(TimerState.Completed, timer.State);
});
Test("Reconfigure a paused timer clears previous deadline", () =>
{
    var clock = new FakeClock(start); var timer = new Countdown(clock); timer.Start(); timer.Pause(); timer.Configure(TimeSpan.FromSeconds(3)); timer.Start(); clock.Advance(3); timer.Tick(); Equal(TimerState.Completed, timer.State);
});
Console.WriteLine($"{passed} tests passed.");

sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    public void Advance(double seconds) => now = now.AddSeconds(seconds);
}
