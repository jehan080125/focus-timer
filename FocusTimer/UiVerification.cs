using FocusTimer.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FocusTimer;

internal static class UiVerification
{
    internal static void Run(string[] args)
    {
        var output = args.SkipWhile(x => x != "--verify-ui").Skip(1).FirstOrDefault() ?? "artifacts/ui";
        Directory.CreateDirectory(output);
        var theme = ((App)Application.Current).Theme;
        var family = (FontFamily)Application.Current.Resources["AppFontFamily"];
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
        {
            Assert(new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var glyphs), "Bundled font resolves without system installation");
            Assert(glyphs.FamilyNames.Values.Any(name => name.Contains("Pretendard")), "Bundled font is Pretendard, not a fallback");
            Assert(glyphs.CharacterToGlyphMap.ContainsKey('시') && glyphs.CharacterToGlyphMap.ContainsKey('0'), "Bundled font covers Korean and timer digits");
        }
        int captures = 0;
        foreach (bool dark in new[] { false, true })
        {
            theme.Apply(dark);
            string name = dark ? "dark" : "light";
            foreach (double opacity in new[] { .1, .5, 1d })
            {
                var prefs = new Preferences { BackgroundOpacity = opacity };
                var timer = new Countdown();
                var window = new MainWindow(prefs, timer, true);
                foreach (double scale in new[] { 1d, 1.5, 2d })
                {
                    string suffix = $"{name}-{opacity * 100:0}-{scale * 100:0}";
                    window.ApplyMode(false, false);
                    Render(window, 420, 480, scale, Path.Combine(output, $"normal-{suffix}.png")); captures++;
                    window.ApplyMode(true, false);
                    window.UpdateMiniControls(false);
                    Render(window, 160, 64, scale, Path.Combine(output, $"mini-{suffix}.png")); captures++;
                    window.UpdateMiniControls(true);
                    Render(window, 160, 64, scale, Path.Combine(output, $"mini-controls-{suffix}.png")); captures++;
                }
                Assert(window.BackgroundSurface.Opacity == opacity, "Background uses the selected opacity");
                Assert(window.NormalContent.Opacity == (1 + opacity) / 2 && window.MiniContent.Opacity == (1 + opacity) / 2 && window.WindowBorder.Opacity == (1 + opacity) / 2, "All foreground layers fade half as much as the background");
                Assert(window.TimeInputs.Opacity == 1 && window.MiniTimeLabel.Opacity == 1, "Clock opacity is not applied twice");
                Assert(((SolidColorBrush)window.HoursInput.Foreground).Color == ((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color, "Timer text inherits the current theme color");
                Assert(window.Topmost, "Mini pinned by default");
                window.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(!window.Topmost, "Mini pin can be disabled");
                window.ApplyMode(false, false);
                Assert(!window.Topmost, "Normal window is not topmost");
                window.MinutesInput.Text = "99";
                Assert(window.MinutesInput.Text == "25", "Invalid minutes are rejected");
                VerifySegmentInput(window.SecondsInput);
                window.MinutesInput.Text = "00"; window.SecondsInput.Text = "00";
                Assert(!window.PlayButton.IsEnabled, "Zero prevents starting");
                window.SecondsInput.Text = "03";
                window.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(timer.State == TimerState.Running && window.SecondsInput.IsReadOnly, "Start locks inline fields without dimming the clock");
                Assert(!window.SecondsInput.TryInsert("12"), "Running clock cannot be edited");
                var duration = timer.Duration;
                window.ApplyMode(true, false);
                theme.Apply(!dark);
                Assert(timer.State == TimerState.Running && timer.Duration == duration, "Mode and theme preserve running timer");
                prefs.LiquidGlass = true; window.ApplyPreferences();
                Assert(!window.HasNativeGlass && window.BackgroundSurface.Opacity == opacity, "Until a real frame arrives, manual transparency remains usable");
                Assert(timer.State == TimerState.Running && timer.Duration == duration, "Glass toggle preserves the timer");
                window.ApplyMode(false, false);
                Assert(prefs.LiquidGlass && !window.HasNativeGlass && !window.IsGlassExcluded && window.WindowRoot.Clip is null, "Normal mode keeps preference but releases glass and exclusion");
                window.ApplyMode(true, false);
                prefs.LiquidGlass = false; window.ApplyPreferences();
                Assert(window.BackgroundSurface.Opacity == opacity && window.MiniContent.Opacity == (1 + opacity) / 2, "Leaving glass restores manual opacity");
                theme.Apply(dark);
                window.MiniPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(timer.State == TimerState.Paused, "Mini pause controls shared timer");
                window.Close();
            }
            var settingsPreferences = new Preferences { BackgroundOpacity = .5 };
            var settings = new SettingsWindow(settingsPreferences, () => { });
            Render(settings, 340, 340, 1, Path.Combine(output, $"settings-{name}.png")); captures++;
            settings.GlassCheck.IsChecked = true;
            Assert(settings.OpacitySlider.IsEnabled, "Normal mode keeps manual opacity even when glass preference is enabled");
            settingsPreferences.Mini = true; settings.UpdateOpacityState();
            Assert(settingsPreferences.LiquidGlass && !settings.OpacitySlider.IsEnabled && settings.OpacityLabel.Text == "자동", "Glass checkbox disables manual opacity");
            settings.OpacitySlider.Value = 10;
            Assert(settingsPreferences.BackgroundOpacity == .5, "Disabled slider cannot overwrite the stored setting");
            settings.OpacitySlider.Value = 50;
            Render(settings, 340, 340, 1, Path.Combine(output, $"settings-glass-{name}.png")); captures++;
            settings.GlassCheck.IsChecked = false;
            Assert(settings.OpacitySlider.IsEnabled && settings.OpacitySlider.Value == 50 && settingsPreferences.BackgroundOpacity == .5, "Manual opacity value survives glass mode");
            settings.Close();
            var glassWindow = new MainWindow(new Preferences { LiquidGlass = true }, new Countdown(), true);
            Render(glassWindow, 420, 480, 1, Path.Combine(output, $"glass-{name}.png")); captures++;
            glassWindow.ApplyMode(true, false); glassWindow.UpdateMiniControls(true);
            Render(glassWindow, 160, 64, 2, Path.Combine(output, $"glass-mini-{name}.png")); captures++; glassWindow.Close();
            var popup = new CompletionWindow();
            Assert(!popup.ShowActivated && popup.Topmost && !popup.ShowInTaskbar, "Popup is non-activating and topmost");
            Render(popup, 320, 168, 1, Path.Combine(output, $"completion-{name}.png")); captures++; popup.Close();
        }
        VerifyNativeWindows();
        string glassResult = GlassVerification.Verify(output, args.Contains("--verify-glass-live"));
        File.WriteAllText(Path.Combine(output, "results.txt"), $"PASS: UI behavior assertions; {captures} WPF renders.\nLight/dark × 10/50/100% opacity × 100/150/200% rendering scales.\nPASS: foreground alpha, mini-only glass preference, manual-opacity restore, native icon, resize hit testing, non-activating completion, notifications and display bounds correction.\n{glassResult}\nPhysical multi-monitor DPI changes, actual sleep/resume and Windows theme setting changes remain manual checks.\n");
    }
    private static void VerifySegmentInput(TimeSegment segment)
    {
        segment.SelectAll(); Assert(segment.TryInsert("12"), "Two-digit replacement accepted");
        Assert(segment.Text == "12", "Both digits are retained");
        Assert(!segment.TryInsert("3") && segment.Text == "12", "Third digit rejected");
        segment.SelectAll(); Assert(!segment.TryInsert("60"), "Minutes/seconds capped at 59");
        Assert(!segment.TryInsert("1a") && !segment.TryInsert("123") && !segment.TryInsert("１２"), "Letters, oversized paste and non-ASCII digits rejected");
        segment.SelectAll(); Assert(segment.TryInsert("7"), "Single digit can be entered");
        segment.Normalize(); Assert(segment.Text == "07", "Single digit normalized on commit");
        segment.Text = ""; segment.Normalize(); Assert(segment.Text == "00", "Empty field normalizes to zero");
    }
    private static void VerifyNativeWindows()
    {
        var clock = new VerificationClock();
        var timer = new Countdown(clock);
        var prefs = new Preferences { Notify = false };
        var window = new MainWindow(prefs, timer, true) { ShowActivated = false, Opacity = 0 };
        window.Show();
        window.UpdateLayout();
        var handle = new WindowInteropHelper(window).Handle;
        Assert(handle != 0, "Real native window created");
        Assert(window.Icon is not null && SendMessage(handle, 0x007F, 1, 0) != 0, "Custom timer icon is assigned to the native window");
        var beforeGlass = GetForegroundWindow();
        prefs.LiquidGlass = true; window.ApplyPreferences();
        Assert(!window.HasNativeGlass && !window.IsGlassExcluded, "Normal mode must never start capture");
        Assert(GetForegroundWindow() == beforeGlass, "Glass surface does not activate the timer");
        prefs.LiquidGlass = false; window.ApplyPreferences();
        Assert(!window.HasNativeGlass, "Disabling glass disposes its native surface");
        GetWindowRect(handle, out var bounds);
        uint point = ((uint)(ushort)(bounds.Top + (bounds.Bottom - bounds.Top) / 2) << 16) | (ushort)(bounds.Left + 2);
        Assert(SendMessage(handle, 0x0084, 0, (nint)point) == 10, "Left edge is a native resize region");
        window.MinutesInput.Text = "00"; window.SecondsInput.Text = "05";
        window.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        clock.Advance();
        window.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(timer.State == TimerState.Paused && window.SecondsInput.Text == "03", "Inline editor displays remaining time on pause");
        Assert(timer.Duration == TimeSpan.FromSeconds(5), "Display synchronization must not replace the original duration");
        window.SecondsInput.Normalize();
        Assert(timer.State == TimerState.Paused, "Leaving an unchanged segment preserves pause state");
        window.SecondsInput.Text = "08";
        Assert(timer.State == TimerState.Ready && timer.Duration == TimeSpan.FromSeconds(8), "Editing paused clock configures the displayed value");
        window.SecondsInput.Text = "9";
        Assert(window.SecondsInput.Text == "9", "Idle refresh does not overwrite an unfinished edit");
        window.SecondsInput.Normalize(); Assert(window.SecondsInput.Text == "09", "Committed display contains exactly two digits");
        timer.Configure(TimeSpan.FromSeconds(1)); timer.Start(); clock.Advance(); timer.Tick();
        Assert(!Application.Current.Windows.OfType<CompletionWindow>().Any(), "Notifications disabled suppress popup");
        prefs.Notify = true;
        window.WindowState = WindowState.Minimized;
        var foreground = GetForegroundWindow();
        timer.Configure(TimeSpan.FromSeconds(1)); timer.Start(); clock.Advance(); timer.Tick();
        var popup = Application.Current.Windows.OfType<CompletionWindow>().Single();
        Assert(popup.IsVisible, "Completion visible while main window is minimized");
        Assert(GetForegroundWindow() == foreground, "Completion does not steal foreground focus");
        Assert((GetWindowLongPtr(new WindowInteropHelper(popup).Handle, -20) & 0x08000000) != 0, "Popup uses WS_EX_NOACTIVATE");
        timer.Tick();
        Assert(Application.Current.Windows.OfType<CompletionWindow>().Count() == 1, "Only one popup per completion");
        popup.Close();
        window.WindowState = WindowState.Normal;
        window.Left = -30000; window.Top = -30000;
        NativeWindow.ClampToScreen(window);
        Assert(window.Left > -30000 && window.Top > -30000, "Offscreen placement corrected");
        window.Close();
    }
    private sealed class VerificationClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(2);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    private static void Assert(bool value, string description) { if (!value) throw new InvalidOperationException(description); }
    private static void Render(Window window, double width, double height, double scale, string path)
    {
        window.Width = width; window.Height = height;
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        VerifyTextFonts(root);
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void VerifyTextFonts(DependencyObject node)
    {
        var family = node switch { TextBlock text => text.FontFamily, Control control => control.FontFamily, _ => null };
        if (family is not null)
            Assert(family.Source.Contains("Pretendard") || family.Source == "Segoe Fluent Icons", $"Unexpected UI font: {family.Source}");
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) VerifyTextFonts(VisualTreeHelper.GetChild(node, i));
    }
}
