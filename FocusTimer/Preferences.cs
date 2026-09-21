using System.IO;
using System.Text.Json;

namespace FocusTimer;

internal sealed record WindowPlacement(double Left, double Top, double Width, double Height);
internal sealed class Preferences
{
    internal const int MinimumGlassFps = 15, MaximumGlassFps = 144, DefaultGlassFps = 60;
    public double BackgroundOpacity { get; set; } = 1;
    public bool LiquidGlass { get; set; }
    public int GlassFps { get; set; } = DefaultGlassFps;
    public bool Notify { get; set; } = true;
    public bool MiniPinned { get; set; } = true;
    public bool Mini { get; set; }
    public double DurationSeconds { get; set; } = 1500;
    public WindowPlacement? NormalPlacement { get; set; }
    public WindowPlacement? MiniPlacement { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusTimer", "settings.json");
    public static Preferences Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            result.BackgroundOpacity = double.IsFinite(result.BackgroundOpacity) ? Math.Clamp(result.BackgroundOpacity, .1, 1) : 1;
            result.GlassFps = Math.Clamp(result.GlassFps, MinimumGlassFps, MaximumGlassFps);
            result.DurationSeconds = double.IsFinite(result.DurationSeconds) ? Math.Clamp(result.DurationSeconds, 0, 359999) : 1500;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
