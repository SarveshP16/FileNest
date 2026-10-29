using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace FileNest.Services;

/// <summary>Small Windows helpers: run at sign-in, Explorer, and window corner rounding.</summary>
public static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FileNest";

    /// <summary>Adds or removes FileNest from the current user's startup programs.</summary>
    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(RunValue, $"\"{exe}\" --minimized");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    public static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    /// <summary>Opens Explorer with the file selected, or the folder if the file is gone.</summary>
    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Path.GetDirectoryName(path) is { } folder)
            OpenFolder(folder);
    }

    /// <summary>Asks Windows 11 to round this window's corners (no-op on Windows 10).</summary>
    public static void UseRoundedCorners(Window window, bool small = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = small ? 3 : 2; // DWMWCP_ROUNDSMALL : DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
    }

    /// <summary>
    /// Gives the window the current theme's material (Mica like File Explorer, or Acrylic glass) and dark/light
    /// frame. The window needs a transparent Background and WindowChrome GlassFrameThickness="-1".
    /// Windows only draws the material while the window is active; inactive windows show a plain tint.
    /// </summary>
    public static void ApplyBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var dark = Ui.ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE

        if (!Ui.ThemeManager.BackdropSupported)
            return;

        // Without this, "Show accent color on title bars" paints our custom title row solid accent,
        // which hides the dark title text and caption buttons.
        var noCaptionColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = DwmSetWindowAttribute(hwnd, 35, ref noCaptionColor, sizeof(int)); // DWMWA_CAPTION_COLOR

        var backdrop = Ui.ThemeManager.Backdrop switch
        {
            Core.Models.AppBackdrop.Mica => 2,    // DWMSBT_MAINWINDOW
            Core.Models.AppBackdrop.Acrylic => 3, // DWMSBT_TRANSIENTWINDOW
            _ => 1,                               // DWMSBT_NONE
        };
        _ = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE

        // WPF paints its own opaque background behind everything unless told not to.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = backdrop == 1 ? Ui.ThemeManager.Parse(Ui.ThemeManager.IsDark ? "#202020" : "#F3F3F3") : System.Windows.Media.Colors.Transparent;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
