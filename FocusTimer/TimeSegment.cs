using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FocusTimer;

// A two-digit part of the clock itself, not a separate form field.
public sealed class TimeSegment : TextBox
{
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(TimeSegment), new PropertyMetadata(99));
    private string lastValid = "";
    private bool restoring;
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Value => int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public TimeSegment()
    {
        MaxLength = 2;
        InputMethod.SetIsInputMethodEnabled(this, false);
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                TryInsert((string)e.DataObject.GetData(DataFormats.UnicodeText));
            e.CancelCommand();
        });
        // Dropping text must obey the same rules as typing and pasting.
        AllowDrop = false;
    }

    private bool Accepts(string text) => text.Length <= 2 && text.All(c => c is >= '0' and <= '9')
        && (text.Length == 0 || int.Parse(text, CultureInfo.InvariantCulture) <= Maximum);

    internal bool TryInsert(string text)
    {
        if (IsReadOnly || text.Length == 0) return false;
        var proposed = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, text);
        if (!Accepts(proposed)) return false;
        SelectedText = text;
        SelectionStart += SelectionLength;
        SelectionLength = 0;
        return true;
    }
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        TryInsert(e.Text);
        e.Handled = true;
        base.OnPreviewTextInput(e);
    }
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (restoring) return;
        if (!Accepts(Text))
        {
            restoring = true; Text = lastValid; restoring = false;
            return;
        }
        lastValid = Text;
        base.OnTextChanged(e);
    }
    internal void Normalize() { if (!IsReadOnly) Text = Value.ToString("00", CultureInfo.InvariantCulture); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); SelectAll(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { Normalize(); base.OnLostKeyboardFocus(e); }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsKeyboardFocusWithin && !IsReadOnly) { Focus(); SelectAll(); e.Handled = true; }
        base.OnPreviewMouseLeftButtonDown(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Normalize(); MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
