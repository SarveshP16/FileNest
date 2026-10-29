// Regenerates src/FileNest/Assets/FileNest.ico (the .exe icon) from the Tabler "folders" glyph.
// Run from the repo root:  dotnet run tools/make-icon.cs
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var fontPath = Path.GetFullPath("src/FileNest/Assets/Fonts/tabler-icons.ttf");
var outPath = Path.GetFullPath("src/FileNest/Assets/FileNest.ico");
var accent = (Color)ColorConverter.ConvertFromString("#0F6E56");
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

var thread = new Thread(() =>
{
    var family = new FontFamily(new Uri(Path.GetDirectoryName(fontPath) + Path.DirectorySeparatorChar), "./#tabler-icons");
    var images = sizes.Select(size => Png(Render(size, family, accent))).ToList();
    WriteIco(outPath, sizes, images);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine($"Wrote {outPath}");

static BitmapSource Render(int size, FontFamily family, Color accent)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        var radius = size * 0.24;
        dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, size, size), radius, radius);
        var text = new FormattedText("", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.66, Brushes.White, 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var b = geometry.Bounds;
        geometry.Transform = new TranslateTransform((size - b.Width) / 2 - b.X, (size - b.Height) / 2 - b.Y);
        dc.DrawGeometry(Brushes.White, null, geometry);
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    return bitmap;
}

static byte[] Png(BitmapSource source)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(source));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}

static void WriteIco(string path, int[] sizes, List<byte[]> images)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((short)0);
    w.Write((short)1);
    w.Write((short)images.Count);
    var offset = 6 + 16 * images.Count;
    for (var i = 0; i < images.Count; i++)
    {
        var s = sizes[i] >= 256 ? 0 : sizes[i];
        w.Write((byte)s);
        w.Write((byte)s);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((short)1);
        w.Write((short)32);
        w.Write(images[i].Length);
        w.Write(offset);
        offset += images[i].Length;
    }

    foreach (var image in images)
        w.Write(image);
}
