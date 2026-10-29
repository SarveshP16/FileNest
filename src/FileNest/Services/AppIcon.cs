using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileNest.Ui;

namespace FileNest.Services;

/// <summary>
/// Draws the FileNest logo (the Tabler "folders" glyph on a rounded square) in the current accent,
/// for the window icon and the tray icon. The .exe icon is a fixed teal file made by tools/make-icon.cs.
/// </summary>
public static class AppIcon
{
    private static readonly FontFamily IconFamily = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#tabler-icons");

    public static BitmapSource Render(int size, Color accent)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var radius = size * 0.24;
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, size, size), radius, radius);
            var text = new FormattedText(Icons.Folders, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(IconFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.66, Brushes.White, 1.0);
            var geometry = text.BuildGeometry(new Point(0, 0));
            var b = geometry.Bounds;
            geometry.Transform = new TranslateTransform((size - b.Width) / 2 - b.X, (size - b.Height) / 2 - b.Y);
            dc.DrawGeometry(Brushes.White, null, geometry);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A GDI icon for the tray. Dispose it (and its handle is destroyed) when replaced.</summary>
    public static TrayIconImage RenderTray(Color accent)
    {
        // 32px renders crisply for the tray at 100–200% scaling; Windows scales it down.
        var source = Render(32, accent);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        return new TrayIconImage(System.Drawing.Icon.FromHandle(handle), handle);
    }

    public sealed class TrayIconImage(System.Drawing.Icon icon, IntPtr handle) : IDisposable
    {
        public System.Drawing.Icon Icon { get; } = icon;

        public void Dispose()
        {
            Icon.Dispose();
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
