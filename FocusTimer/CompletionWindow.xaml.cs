using System.Windows;

namespace FocusTimer;

public partial class CompletionWindow : Window
{
    public CompletionWindow()
    {
        InitializeComponent();
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 20; Top = work.Bottom - Height - 20;
        SourceInitialized += (_, _) => NativeWindow.NoActivate(this);
        Loaded += (_, _) => NativeWindow.ClampToScreen(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
