using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.IO;

namespace FocusTimer;

public partial class SettingsWindow : Window
{
    private readonly Preferences preferences;
    private readonly Action changed;
    private readonly Func<bool> glassActive;
    private readonly Func<string?> glassError;
    private bool initialized;
    internal SettingsWindow(Preferences preferences, Action changed, Func<bool>? glassActive = null, Func<string?>? glassError = null)
    {
        this.preferences = preferences; this.changed = changed;
        this.glassActive = glassActive ?? (() => preferences.Mini && preferences.LiquidGlass);
        this.glassError = glassError ?? (() => null);
        InitializeComponent();
        FpsSlider.Minimum = Preferences.MinimumGlassFps;
        FpsSlider.Maximum = Preferences.MaximumGlassFps;
        FpsSlider.Value = preferences.GlassFps;
        FpsLabel.Text = $"{preferences.GlassFps} fps";
        OpacitySlider.Value = preferences.BackgroundOpacity * 100;
        NotifyCheck.IsChecked = preferences.Notify;
        GlassCheck.IsChecked = preferences.LiquidGlass;
        UpdateOpacityState();
        initialized = true;
        Loaded += (_, _) => NativeWindow.ClampToScreen(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized || glassActive()) return;
        preferences.BackgroundOpacity = e.NewValue / 100;
        OpacityLabel.Text = $"{e.NewValue:0}%"; changed();
    }
    private void Notify_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        preferences.Notify = NotifyCheck.IsChecked == true; changed();
    }
    private void Fps_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized) return;
        preferences.GlassFps = Math.Clamp((int)Math.Round(e.NewValue), Preferences.MinimumGlassFps, Preferences.MaximumGlassFps);
        FpsLabel.Text = $"{preferences.GlassFps} fps";
        changed();
    }
    private void Glass_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        preferences.LiquidGlass = GlassCheck.IsChecked == true;
        UpdateOpacityState();
        changed();
    }
    internal void UpdateOpacityState()
    {
        FpsSlider.IsEnabled = preferences.LiquidGlass;
        OpacitySlider.IsEnabled = !glassActive();
        // Restore the stored manual value after leaving glass mode.
        OpacitySlider.Value = preferences.BackgroundOpacity * 100;
        OpacityLabel.Text = glassActive() ? "자동" : $"{OpacitySlider.Value:0}%";
        GlassStatus.Text = glassError() ?? "";
        GlassStatus.Visibility = string.IsNullOrEmpty(GlassStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            e.Handled = NativeWindow.BeginFreeDrag(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void License_Click(object sender, RoutedEventArgs e)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/FocusTimer;component/Assets/Fonts/OFL.txt")).Stream;
        using var reader = new StreamReader(stream);
        var text = new TextBox
        {
            Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 13, Padding = new Thickness(18), BorderThickness = new Thickness(0)
        };
        var license = new Window
        {
            Style = (Style)Application.Current.FindResource(typeof(Window)),
            Title = "Pretendard · SIL Open Font License", Width = 580, Height = 520,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text
        };
        license.Show();
    }
}
