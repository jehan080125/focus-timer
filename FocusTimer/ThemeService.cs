using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusTimer;

internal sealed class ThemeService : IDisposable
{
    private readonly DispatcherTimer fallback = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool? dark;
    public bool IsDark => dark == true;
    public event Action? Changed;
    public void Start()
    {
        Refresh();
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        fallback.Tick += Poll;
        fallback.Start();
    }
    private void Poll(object? sender, EventArgs e) => Refresh();
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        bool value = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            value = key?.GetValue("AppsUseLightTheme") is int setting && setting == 0;
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        if (dark != value) Apply(value);
    }
    internal void Apply(bool isDark)
    {
        dark = isDark;
        var colors = isDark
            ? new[] { "#202226", "#F3F4F6", "#A8ADB5", "#3A3D43", "#292C31", "#383E48", "#26292E", "#61AEFF", "#102239", "#343940" }
            : new[] { "#F3F8FB", "#292E35", "#687480", "#D9E2E9", "#FFFFFF", "#DFECF7", "#FFFFFF", "#006DC7", "#FFFFFF", "#E0EAF0" };
        var names = new[] { "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "ButtonBrush", "HoverBrush", "InputBrush", "AccentBrush", "AccentTextBrush", "TrackBrush" };
        for (int i = 0; i < names.Length; i++)
            Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Brush Solid(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Application.Current.Resources["GlassSurfaceBrush"] = Solid(isDark ? "#40202A39" : "#18F4F9FF");
        Application.Current.Resources["GlassButtonBrush"] = Solid(isDark ? "#304A5D77" : "#60FFFFFF");
        Application.Current.Resources["GlassHoverBrush"] = Solid(isDark ? "#80586F8F" : "#B0FFFFFF");
        Application.Current.Resources["GlassTrackBrush"] = Solid(isDark ? "#386D8DA9" : "#30FFFFFF");
        Application.Current.Resources["GlassBorderBrush"] = Solid(isDark ? "#40FFFFFF" : "#80FFFFFF");
        Application.Current.Resources["GlassSheenBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(14, 255, 255, 255), 0),
            new(Color.FromArgb(0, 255, 255, 255), .45),
            new(Color.FromArgb(8, 152, 204, 255), 1)
        }, new Point(0, 0), new Point(1, 1));
        Application.Current.Resources["GlassRimBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(110, 224, 237, 250), 0),
            new(Color.FromArgb(45, 131, 157, 181), .5),
            new(Color.FromArgb(70, 188, 213, 237), 1)
        }, new Point(0, 0), new Point(1, 1));
        Changed?.Invoke();
    }
    public void Dispose()
    {
        fallback.Stop();
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        fallback.Tick -= Poll;
    }
}
