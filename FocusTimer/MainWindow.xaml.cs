using FocusTimer.Core;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusTimer;

public partial class MainWindow : Window
{
    private readonly Countdown countdown;
    private readonly Preferences preferences;
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly bool verification;
    private bool initialized, syncing, mini, keyboardInteraction;
    private SettingsWindow? settingsWindow;
    private CompletionWindow? completionWindow;
    private GlassBackdrop? glassBackdrop;

    public MainWindow() : this(Preferences.Load(), new Countdown(), false) { }
    internal MainWindow(Preferences preferences, Countdown countdown, bool verification)
    {
        this.preferences = preferences;
        this.countdown = countdown;
        this.verification = verification;
        InitializeComponent();
        countdown.Configure(TimeSpan.FromSeconds(preferences.DurationSeconds));
        countdown.Completed += OnCompleted;
        SyncInputs();
        initialized = true;
        ApplyMode(preferences.Mini, false);
        ApplyPreferences();
        Refresh();
        ticker.Tick += Tick;
        if (!verification) ticker.Start();
        SourceInitialized += (_, _) => { NativeWindow.ClampToScreen(this); UpdateBackdrop(); };
        Loaded += (_, _) => NativeWindow.ClampToScreen(this);
        SizeChanged += (_, _) => { UpdateGlassClip(); UpdateMiniControls(); };
        Closed += OnClosed;
        Deactivated += (_, _) => { keyboardInteraction = false; UpdateMiniControls(); };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        ((App)Application.Current).Theme.Changed += ThemeChanged;
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() => NativeWindow.ClampToScreen(this)));
    private void Tick(object? sender, EventArgs e) { countdown.Tick(); Refresh(); }
    private void SyncInputs(TimeSpan? display = null)
    {
        syncing = true;
        var parts = Countdown.Format(display ?? countdown.Duration).Split(':');
        if (HoursInput.Text != parts[0]) HoursInput.Text = parts[0];
        if (MinutesInput.Text != parts[1]) MinutesInput.Text = parts[1];
        if (SecondsInput.Text != parts[2]) SecondsInput.Text = parts[2];
        syncing = false;
    }

    private void TimeInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized || syncing || countdown.State == TimerState.Running) return;
        countdown.Configure(new TimeSpan(HoursInput.Value, MinutesInput.Value, SecondsInput.Value));
        preferences.DurationSeconds = countdown.Duration.TotalSeconds;
        CloseCompletion();
        Refresh();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (countdown.State == TimerState.Running) countdown.Pause();
        else
        {
            if (countdown.State == TimerState.Completed) countdown.Reset();
            countdown.Start();
            CloseCompletion();
        }
        Refresh();
        Persist();
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        countdown.Reset(); SyncInputs(); CloseCompletion(); Refresh(); Persist();
    }
    private void Hour_Click(object sender, RoutedEventArgs e) => StartAtMinute(0);
    private void HalfHour_Click(object sender, RoutedEventArgs e) => StartAtMinute(30);
    private void StartAtMinute(int minute)
    {
        countdown.StartUntil(Countdown.NextClockTime(DateTimeOffset.Now, minute));
        preferences.DurationSeconds = countdown.Duration.TotalSeconds;
        SyncInputs(); CloseCompletion(); Refresh(); Persist();
    }
    private void Refresh()
    {
        var running = countdown.State == TimerState.Running;
        var completed = countdown.State == TimerState.Completed;
        foreach (var segment in new[] { HoursInput, MinutesInput, SecondsInput })
        {
            segment.IsReadOnly = running;
            segment.Focusable = !running;
            segment.IsHitTestVisible = !running;
        }
        if (countdown.State != TimerState.Ready) SyncInputs(countdown.Remaining);
        MiniTimeLabel.Text = Countdown.Format(countdown.Remaining);
        ProgressRing.Progress = countdown.Duration.TotalSeconds > 0 ? countdown.Remaining.TotalSeconds / countdown.Duration.TotalSeconds : 0;
        PlayIcon.Text = running ? "\uE769" : "\uE768";
        MiniPlayButton.Content = PlayIcon.Text;
        PlayText.Text = running ? "일시정지" : countdown.State == TimerState.Paused ? "계속" : completed ? "다시 시작" : "시작";
        PlayButton.IsEnabled = MiniPlayButton.IsEnabled = running || countdown.Duration > TimeSpan.Zero;
        MiniTimeLabel.SetResourceReference(TextBlock.ForegroundProperty, completed ? "AccentBrush" : "TextBrush");
    }

    private void OnCompleted()
    {
        if (!preferences.Notify) return;
        CloseCompletion();
        completionWindow = new CompletionWindow();
        completionWindow.Closed += (_, _) => completionWindow = null;
        completionWindow.Show();
    }
    private void CloseCompletion() { completionWindow?.Close(); }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(preferences, () => { ApplyPreferences(); Persist(); }, () => HasNativeGlass, () => glassBackdrop?.Error) { Owner = this };
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    internal void ApplyPreferences()
    {
        UpdateBackdrop();
        ApplyAppearance();
    }
    private void ApplyAppearance()
    {
        bool glass = mini && preferences.LiquidGlass && HasNativeGlass;
        BackgroundSurface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        BackgroundSurface.Visibility = glass ? Visibility.Collapsed : Visibility.Visible;
        BackgroundSurface.Opacity = glass ? 1 : preferences.BackgroundOpacity;
        // Apply once to each foreground layer, so nested text is not faded twice.
        NormalContent.Opacity = MiniContent.Opacity = WindowBorder.Opacity = glass ? 1 : (1 + preferences.BackgroundOpacity) / 2;
        TimeInputs.Opacity = MiniTimeLabel.Opacity = 1;
        GlassDecoration.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        WindowBorder.Visibility = glass ? Visibility.Collapsed : Visibility.Visible;
        foreach (var key in new[] { "ButtonBrush", "HoverBrush", "TrackBrush", "BorderBrush" })
        {
            if (glass) Resources[key] = Application.Current.Resources["Glass" + key];
            else Resources.Remove(key);
        }
        UpdateGlassClip();
        settingsWindow?.UpdateOpacityState();
        Topmost = mini && preferences.MiniPinned;
        PinButton.SetResourceReference(Control.ForegroundProperty, preferences.MiniPinned ? "AccentBrush" : "MutedBrush");
        PinButton.ToolTip = preferences.MiniPinned ? "항상 위: 켜짐 · 다른 창보다 위에 표시" : "항상 위: 꺼짐";
        if (!preferences.Notify) CloseCompletion();
    }
    private void ThemeChanged() => ApplyPreferences();
    private void UpdateBackdrop()
    {
        if (preferences.LiquidGlass && mini)
        {
            glassBackdrop ??= new GlassBackdrop(this, GlassImage, ApplyAppearance);
            glassBackdrop.UpdateFrameRate(preferences.GlassFps);
            glassBackdrop.UpdateTheme(((App)Application.Current).Theme.IsDark);
        }
        else { var old = glassBackdrop; glassBackdrop = null; old?.Dispose(); }
    }
    internal bool HasNativeGlass => glassBackdrop?.IsAvailable == true;
    internal bool IsGlassExcluded => glassBackdrop?.IsExcluded == true;
    internal string? GlassError => glassBackdrop?.Error;
    private void UpdateGlassClip()
    {
        bool glass = mini && preferences.LiquidGlass && HasNativeGlass;
        var width = Math.Max(0, ActualWidth); var height = Math.Max(0, ActualHeight);
        double radius = Math.Min(width, height) / 2;
        WindowRoot.Clip = glass ? new RectangleGeometry(new Rect(0, 0, width, height), radius, radius) : null;
        // Leave space for the pill ends; six buttons still fit at 160×64.
        MiniToolbar.Margin = glass ? new Thickness(0, 0, 0, 5) : new Thickness(0);
        foreach (Button button in MiniToolbar.Children)
            button.Width = glass ? 22 : 24;
        UpdateMiniControls();
    }
    private void Pin_Click(object sender, RoutedEventArgs e) { preferences.MiniPinned = !preferences.MiniPinned; ApplyPreferences(); Persist(); }
    private void Mode_Click(object sender, RoutedEventArgs e) => ApplyMode(!mini, true);
    internal void ApplyMode(bool compact, bool saveCurrent)
    {
        if (saveCurrent) CapturePlacement();
        mini = preferences.Mini = compact;
        keyboardInteraction = false;
        NormalContent.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MiniContent.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = compact ? 120 : 380;
        MinHeight = compact ? 40 : 480;
        var placement = compact ? preferences.MiniPlacement : preferences.NormalPlacement;
        Width = SafeSize(placement?.Width, compact ? 240 : 420, MinWidth);
        Height = SafeSize(placement?.Height, compact ? 88 : 480, MinHeight);
        if (placement is not null && double.IsFinite(placement.Left) && double.IsFinite(placement.Top))
        { Left = placement.Left; Top = placement.Top; }
        else if (!saveCurrent) { Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2; Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2; }
        ApplyPreferences();
        UpdateMiniControls();
        if (IsLoaded) NativeWindow.ClampToScreen(this);
        if (saveCurrent) Persist();
    }
    private static double SafeSize(double? value, double fallback, double minimum) => value is double v && double.IsFinite(v) ? Math.Clamp(v, minimum, 2000) : fallback;
    private void CapturePlacement()
    {
        if (WindowState != WindowState.Normal) return;
        var placement = new WindowPlacement(Left, Top, Width, Height);
        if (mini) preferences.MiniPlacement = placement; else preferences.NormalPlacement = placement;
    }
    private void Persist()
    {
        if (verification) return;
        CapturePlacement();
        NormalContent.ToolTip = preferences.Save() ? null : "설정을 저장하지 못했습니다";
    }
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is Button or TextBox) return;
            source = VisualTreeHelper.GetParent(source);
        }
        e.Handled = NativeWindow.BeginFreeDrag(this, Persist);
    }
    private void Window_HoverChanged(object sender, MouseEventArgs e) => UpdateMiniControls();
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) { keyboardInteraction = false; UpdateMiniControls(); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (mini && e.Key == Key.Tab)
        {
            bool wasHidden = MiniToolbar.Visibility != Visibility.Visible;
            keyboardInteraction = true; UpdateMiniControls();
            if (wasHidden) { MiniPlayButton.Focus(); e.Handled = true; }
        }
        else if (e.Key == Key.Escape) { keyboardInteraction = false; Keyboard.ClearFocus(); UpdateMiniControls(); }
    }
    internal void UpdateMiniControls(bool? showOverride = null)
    {
        if (!initialized || !mini) return;
        bool show = showOverride ?? (IsMouseOver || keyboardInteraction);
        bool tiny = (ActualWidth > 0 ? ActualWidth : Width) < 160 || (ActualHeight > 0 ? ActualHeight : Height) < 64;
        MiniContent.Margin = new Thickness(tiny ? 4 : 7);
        MiniToolbar.VerticalAlignment = tiny ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        MiniToolbar.Margin = !tiny && HasNativeGlass ? new Thickness(0, 0, 0, 5) : new Thickness(0);
        foreach (var button in new[] { MiniResetButton, MiniSettingsButton, MiniCloseButton })
            button.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        MiniToolbar.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        MiniTimeBox.Visibility = tiny && show ? Visibility.Hidden : Visibility.Visible;
        MiniTimeBox.Margin = !tiny && show ? new Thickness(5, 0, 5, 26) : new Thickness(5, 0, 5, 0);
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OnClosed(object? sender, EventArgs e)
    {
        CloseCompletion();
        ticker.Stop(); ticker.Tick -= Tick;
        countdown.Completed -= OnCompleted;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        ((App)Application.Current).Theme.Changed -= ThemeChanged;
        glassBackdrop?.Dispose();
        Persist();
    }
}
