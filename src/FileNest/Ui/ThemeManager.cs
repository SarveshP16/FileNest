using System.Windows;
using System.Windows.Media;
using FileNest.Core.Models;
using Microsoft.Win32;

namespace FileNest.Ui;

/// <summary>A colour ramp for one nest colour: tinted icon background, icon colour, and the bar segment colour.</summary>
public sealed record NestPalette(string LightBg, string LightFg, string DarkBg, string DarkFg, string Bar);

/// <summary>An accent: the solid fill, its text colour on light/dark surfaces, and its tint on light/dark surfaces.</summary>
public sealed record AccentPalette(string Fill, string LightText, string DarkText, string LightBg, string DarkBg);

/// <summary>
/// Owns every colour in the app. Brushes live in Application.Resources under the keys below and are
/// referenced with DynamicResource, so switching theme or accent repaints everything live.
/// </summary>
public static class ThemeManager
{
    public static readonly IReadOnlyDictionary<string, AccentPalette> Accents = new Dictionary<string, AccentPalette>
    {
        ["Teal"] = new("#0F6E56", "#0F6E56", "#5DCAA5", "#E1F5EE", "#085041"),
        ["Purple"] = new("#534AB7", "#534AB7", "#AFA9EC", "#EEEDFE", "#3C3489"),
        ["Coral"] = new("#D85A30", "#993C1D", "#F0997B", "#FAECE7", "#712B13"),
        ["Pink"] = new("#D4537E", "#993556", "#ED93B1", "#FBEAF0", "#72243E"),
        ["Green"] = new("#3B6D11", "#3B6D11", "#97C459", "#EAF3DE", "#27500A"),
        ["Amber"] = new("#BA7517", "#854F0B", "#EF9F27", "#FAEEDA", "#633806"),
        ["Blue"] = new("#185FA5", "#185FA5", "#85B7EB", "#E6F1FB", "#0C447C"),
    };

    public static readonly IReadOnlyDictionary<string, NestPalette> NestColors = new Dictionary<string, NestPalette>
    {
        ["Red"] = new("#FCEBEB", "#A32D2D", "#5C1A1A", "#F7C1C1", "#E24B4A"),
        ["Blue"] = new("#E6F1FB", "#185FA5", "#0C447C", "#B5D4F4", "#378ADD"),
        ["Amber"] = new("#FAEEDA", "#854F0B", "#633806", "#FAC775", "#BA7517"),
        ["Purple"] = new("#EEEDFE", "#534AB7", "#3C3489", "#CECBF6", "#7F77DD"),
        ["Green"] = new("#EAF3DE", "#3B6D11", "#27500A", "#C0DD97", "#639922"),
        ["Gray"] = new("#F1EFE8", "#5F5E5A", "#444441", "#D3D1C7", "#888780"),
        ["Teal"] = new("#E1F5EE", "#0F6E56", "#085041", "#9FE1CB", "#1D9E75"),
        ["Pink"] = new("#FBEAF0", "#993556", "#72243E", "#F4C0D1", "#D4537E"),
        ["Coral"] = new("#FAECE7", "#993C1D", "#712B13", "#F5C4B3", "#D85A30"),
    };

    public static bool IsDark { get; private set; }

    public static string AccentName { get; private set; } = "Teal";

    public static Color AccentColor => Parse(Accent.Fill);

    private static AccentPalette Accent => Accents.TryGetValue(AccentName, out var a) ? a : Accents["Teal"];

    /// <summary>Raised after brushes change, for things that can't use DynamicResource (icons, item lists).</summary>
    public static event Action? Changed;

    private static AppTheme _mode;

    /// <summary>The window material in use. Falls back to Solid where Windows can't draw Mica/Acrylic.</summary>
    public static AppBackdrop Backdrop { get; private set; } = AppBackdrop.Mica;

    /// <summary>Mica and Acrylic window backdrops need Windows 11 22H2 (build 22621) or later.</summary>
    public static bool BackdropSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    public static void Initialize(AppSettings settings)
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _mode == AppTheme.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_mode, AccentName, Backdrop));
        };
        Apply(settings.Theme, settings.Accent, settings.Backdrop);
    }

    public static void Apply(AppTheme mode, string accent, AppBackdrop backdrop)
    {
        _mode = mode;
        AccentName = Accents.ContainsKey(accent) ? accent : "Teal";
        Backdrop = BackdropSupported ? backdrop : AppBackdrop.Solid;
        IsDark = mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme(),
        };

        var r = Application.Current.Resources;
        var a = Accent;
        var d = IsDark;
        var glass = Backdrop != AppBackdrop.Solid;

        // Neutral Windows 11 (Fluent) greys. With a backdrop, surfaces are translucent so Mica/Acrylic shows through.
        Set(r, "Surface0Brush", d ? "#202020" : "#F3F3F3");
        Set(r, "WindowBrush", glass ? "#00000000" : d ? "#202020" : "#F3F3F3");
        Set(r, "LayerBrush", glass ? (d ? "#4C3A3A3A" : "#80FFFFFF") : (d ? "#272727" : "#F9F9F9"));
        Set(r, "Surface1Brush", d ? "#0FFFFFFF" : "#0A000000");
        Set(r, "Surface2Brush", glass ? (d ? "#0DFFFFFF" : "#B3FFFFFF") : (d ? "#2B2B2B" : "#FFFFFF"));
        Set(r, "CardHoverBrush", glass ? (d ? "#17FFFFFF" : "#E6FFFFFF") : (d ? "#323232" : "#FAFAFA"));
        Set(r, "SolidCardBrush", d ? "#2C2C2C" : "#FBFBFB");
        Set(r, "LineBrush", d ? "#17FFFFFF" : "#14000000");
        Set(r, "LineStrongBrush", d ? "#2EFFFFFF" : "#29000000");
        Set(r, "HoverBrush", d ? "#0FFFFFFF" : "#0A000000");
        Set(r, "PressedBrush", d ? "#0AFFFFFF" : "#06000000");
        Set(r, "TextPrimaryBrush", d ? "#FFFFFF" : "#1B1B1B");
        Set(r, "TextSecondaryBrush", d ? "#D6D6D6" : "#4F4F4F");
        Set(r, "TextMutedBrush", d ? "#ABABAB" : "#6A6A6A");

        Set(r, "AccentBrush", a.Fill);
        Set(r, "AccentHoverBrush", Shade(a.Fill, d ? 1.12 : 0.9));
        Set(r, "OnAccentBrush", "#FFFFFF");
        Set(r, "AccentTextBrush", d ? a.DarkText : a.LightText);
        Set(r, "AccentBgBrush", d ? a.DarkBg : a.LightBg);
        Set(r, "AccentTrackBrush", WithAlpha(d ? a.DarkText : a.Fill, 0x33));

        Set(r, "SuccessBgBrush", d ? "#27500A" : "#EAF3DE");
        Set(r, "SuccessTextBrush", d ? "#C0DD97" : "#3B6D11");
        Set(r, "SuccessBorderBrush", d ? "#3B6D11" : "#C0DD97");
        Set(r, "WarningBgBrush", d ? "#633806" : "#FAEEDA");
        Set(r, "WarningTextBrush", d ? "#FAC775" : "#854F0B");
        Set(r, "WarningBorderBrush", d ? "#854F0B" : "#FAC775");
        Set(r, "DangerBgBrush", d ? "#5C1A1A" : "#FCEBEB");
        Set(r, "DangerTextBrush", d ? "#F7C1C1" : "#A32D2D");
        Set(r, "DangerBorderBrush", d ? "#A32D2D" : "#F7C1C1");
        Set(r, "CloseHoverBrush", "#C42B1C");

        Changed?.Invoke();
    }

    public static NestPalette Nest(string? key) =>
        key is not null && NestColors.TryGetValue(key, out var p) ? p : NestColors["Gray"];

    public static Brush NestBackground(string? key) => Frozen(IsDark ? Nest(key).DarkBg : Nest(key).LightBg);

    public static Brush NestForeground(string? key) => Frozen(IsDark ? Nest(key).DarkFg : Nest(key).LightFg);

    public static Brush NestBar(string? key) => Frozen(Nest(key).Bar);

    public static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush(Parse(hex));
        brush.Freeze();
        return brush;
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Set(ResourceDictionary resources, string key, string hex) => resources[key] = Frozen(hex);

    private static string Shade(string hex, double factor)
    {
        var c = Parse(hex);
        byte S(byte v) => (byte)Math.Clamp(v * factor, 0, 255);
        return $"#{S(c.R):X2}{S(c.G):X2}{S(c.B):X2}";
    }

    private static string WithAlpha(string hex, byte alpha)
    {
        var c = Parse(hex);
        return $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
