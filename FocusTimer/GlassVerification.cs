using FocusTimer.Core;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FocusTimer;

internal static class GlassVerification
{
    public static string Verify(string directory, bool live)
    {
        int count = 0;
        foreach (int pattern in new[] { 1, 2, 3, 4 })
        foreach (bool dark in new[] { false, true })
        foreach (double scale in new[] { 1d, 1.5, 2d })
        {
            int width = (int)(160 * scale), height = (int)(64 * scale);
            var pixels = Task.Run(() => Render(pattern, width, height, (float)scale, dark)).GetAwaiter().GetResult();
            CheckMask(pixels, width, height);
            var bitmap = BitmapSource.Create(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null, pixels, width * 4);
            Save(bitmap, Path.Combine(directory, $"glass-shader-{pattern}-{dark}-{scale * 100:0}.png"));
            if (pattern == 1)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(bitmap, new Rect(0, 0, 160, 64));
                    var font = new Typeface((FontFamily)Application.Current.Resources["AppFontFamily"], FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    var text = new FormattedText("00:26:58", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 29,
                        dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(40, 46, 54)), scale);
                    dc.DrawText(text, new Point((160 - text.Width) / 2, (64 - text.Height) / 2));
                }
                var preview = new RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);
                preview.Render(visual); Save(preview, Path.Combine(directory,$"glass-preview-{dark}-{scale * 100:0}.png"));
            }
            count++;
        }
        string liveResult = live ? VerifyLive() : "Live WGC capture: not requested (use --verify-ui <dir> --verify-glass-live).";
        return $"PASS: {count} native D3D shader renders, capsule alpha and premultiplied edge checks, animated source changes.\n{liveResult}";
    }
    private static byte[] Render(int pattern, int width, int height, float scale, bool dark)
    {
        Marshal.ThrowExceptionForHR(GlassBackdrop.GlassCreate(pattern, out var renderer));
        try
        {
            var pixels = new byte[width * height * 4];
            Marshal.ThrowExceptionForHR(GlassBackdrop.GlassFrame(renderer,0,0,width,height,scale,dark?1:0,pixels,pixels.Length));
            if (pattern == 1)
            {
                var moved = new byte[pixels.Length];
                Marshal.ThrowExceptionForHR(GlassBackdrop.GlassFrame(renderer,11,7,width,height,scale,dark?1:0,moved,moved.Length));
                Require(!pixels.SequenceEqual(moved), "Changing backdrop must update the rendered glass");
                // Clear center retains distinct colors rather than flattening into a gray fill.
                int row = height/2, a = (row*width + width/2)*4, b = (row*width+width/2+24)*4;
                Require(Math.Abs(pixels[a]-pixels[b]) > 50, "Center must retain background contrast");
            }
            return pixels;
        }
        finally { GlassBackdrop.GlassDestroy(renderer); }
    }
    private static void CheckMask(byte[] data, int width, int height)
    {
        double radius = height/2d;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            double qx=x+.5-width/2d, qy=y+.5-height/2d;
            double dx=qx-Math.Clamp(qx,-width/2d+radius,width/2d-radius);
            double distance=radius-Math.Sqrt(dx*dx+qy*qy);
            int index=(y*width+x)*4, alpha=data[index+3];
            if(distance < -.5) Require(alpha==0,"No rectangular pixels outside capsule");
            if(distance > 2) Require(alpha==255,"Interior is completely rendered");
            Require(data[index]<=alpha && data[index+1]<=alpha && data[index+2]<=alpha,"Edges must use premultiplied alpha");
        }
    }
    private static string VerifyLive()
    {
        // Do not save screen pixels: the live test checks liveness and lifecycle only.
        var background = new Window { Width=420,Height=240,WindowStyle=WindowStyle.None,ShowActivated=false,
            ShowInTaskbar=false,Topmost=true,Background=Brushes.CornflowerBlue };
        var prefs = new Preferences { Mini=true,LiquidGlass=true,BackgroundOpacity=.5 };
        var timer = new Countdown();
        var window = new MainWindow(prefs,timer,true) { ShowActivated=false };
        background.Show(); window.Show();
        window.Left=background.Left+70; window.Top=background.Top+70;
        try
        {
            PumpUntil(() => window.HasNativeGlass || window.GlassError is not null,15);
            if(!window.HasNativeGlass)
            {
                Require(!window.IsGlassExcluded && window.BackgroundSurface.Visibility==Visibility.Visible,"Failure must restore manual mode and capture eligibility");
                return "Live WGC fallback (not a rendering pass): " + window.GlassError;
            }
            Require(window.IsGlassExcluded && window.WindowRoot.Clip is RectangleGeometry,"Live glass requires capture exclusion and matching clip");
            PumpUntil(() => CenterMatches(window, Colors.CornflowerBlue), 3);
            Require(CenterMatches(window, Colors.CornflowerBlue), "Live pixels must match the controlled background, not the timer itself");
            background.Background=Brushes.Coral;
            PumpUntil(() => CenterMatches(window, Colors.Coral),3);
            Require(CenterMatches(window, Colors.Coral),"Live glass updates when the background changes");
            window.Width=160; window.Height=64; window.Left+=20; window.Top+=20;
            PumpUntil(() => window.GlassImage.Source is BitmapSource source && source.PixelWidth==(int)Math.Round(160*VisualTreeHelper.GetDpi(window).DpiScaleX),3);
            Require(CenterMatches(window, Colors.Coral),"Moving and resizing keeps background coordinates correct");
            window.ApplyMode(false,false);
            Require(!window.HasNativeGlass && !window.IsGlassExcluded && window.BackgroundSurface.Opacity==.5,"Normal mode releases live glass and restores opacity");
            window.ApplyMode(true,false);
            PumpUntil(() => window.HasNativeGlass || window.GlassError is not null,15);
            Require(window.HasNativeGlass,"Returning to mini restarts glass");
            window.WindowState=WindowState.Minimized;
            Require(!window.IsGlassExcluded && !window.HasNativeGlass,"Minimizing releases capture");
            window.WindowState=WindowState.Normal;
            PumpUntil(() => window.HasNativeGlass || window.GlassError is not null,15);
            Require(window.HasNativeGlass,"Restoring restarts glass");
            prefs.LiquidGlass=false; window.ApplyPreferences();
            Require(!window.IsGlassExcluded && !window.HasNativeGlass,"Switch off restores capture eligibility");
            return "PASS: live WGC pixels match changing controlled backgrounds; move/resize, mini/normal transition, minimize/restore and exclusion cleanup. No screen images saved.";
        }
        finally { window.Close(); background.Close(); }
    }
    private static bool CenterMatches(MainWindow window, Color expected)
    {
        if(window.GlassImage.Source is not BitmapSource source) return false;
        var pixel=new byte[4]; source.CopyPixels(new Int32Rect(source.PixelWidth/2,source.PixelHeight/2,1,1),pixel,4,0);
        bool dark=((App)Application.Current).Theme.IsDark;
        double amount=.10;
        byte r=(byte)Math.Round(expected.R*(1-amount)+(dark?.06:.95)*255*amount);
        byte g=(byte)Math.Round(expected.G*(1-amount)+(dark?.085:.98)*255*amount);
        byte b=(byte)Math.Round(expected.B*(1-amount)+(dark?.12:1)*255*amount);
        return Math.Abs(pixel[0]-b)<5 && Math.Abs(pixel[1]-g)<5 && Math.Abs(pixel[2]-r)<5 && pixel[3]==255;
    }
    private static void PumpUntil(Func<bool> ready, int seconds)
    {
        var end=DateTime.UtcNow.AddSeconds(seconds);
        while(!ready() && DateTime.UtcNow<end)
        {
            var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;}; timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
    private static void Save(BitmapSource image,string path)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var file=File.Create(path);encoder.Save(file);
    }
    private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
}
