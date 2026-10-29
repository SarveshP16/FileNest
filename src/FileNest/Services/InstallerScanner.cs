using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FileNest.Core.Services;
using Microsoft.Win32;

namespace FileNest.Services;

/// <summary>
/// Finds installer files whose app is already installed: reads Windows' installed-programs list from the
/// registry and each installer's product name, then asks <see cref="InstallerMatcher"/>.
/// </summary>
public static class InstallerScanner
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Scans the top level of each folder. Slow-ish (reads every installer's metadata); run off the UI thread.</summary>
    public static List<InstallerMatch> Scan(IEnumerable<string> folders, ICollection<string> kept)
    {
        var apps = ReadInstalledApps();
        var matches = new List<InstallerMatch>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var path in SafeEnumerate(folder))
            {
                if (kept.Contains(path, StringComparer.OrdinalIgnoreCase))
                    continue;
                var info = new FileInfo(path);
                var installer = new InstallerFile(path, ReadProductName(path), info.Length, info.LastWriteTime);
                if (InstallerMatcher.Match(installer, apps) is { } match)
                    matches.Add(match);
            }
        }

        return matches;
    }

    public static List<InstalledApp> ReadInstalledApps()
    {
        var apps = new List<InstalledApp>();
        Read(Registry.LocalMachine, UninstallKey);
        Read(Registry.LocalMachine, UninstallKey32);
        Read(Registry.CurrentUser, UninstallKey);
        return apps;

        void Read(RegistryKey hive, string path)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
                return;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display))
                    continue;
                // Hidden components and patches aren't apps the user installed from a download.
                if (key.GetValue("SystemComponent") is int hidden && hidden == 1 || key.GetValue("ParentKeyName") is not null)
                    continue;
                apps.Add(new InstalledApp(display, key.GetValue("Publisher") as string, key.GetValue("DisplayVersion") as string,
                    ParseInstallDate(key.GetValue("InstallDate") as string)));
            }
        }
    }

    private static DateTime? ParseInstallDate(string? value) =>
        DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>The product name an installer reports about itself: version info for .exe, the Property table for .msi.</summary>
    public static string? ReadProductName(string path)
    {
        try
        {
            if (path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                return ReadMsiProperty(path, "ProductName");

            var info = FileVersionInfo.GetVersionInfo(path);
            return FirstUseful(info.ProductName, info.FileDescription);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    private static string? FirstUseful(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && InstallerMatcher.Tokens(v).Count > 0)?.Trim();

    private static IEnumerable<string> SafeEnumerate(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return [];
            return Directory.EnumerateFiles(folder)
                .Where(f => InstallerMatcher.InstallerExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .Where(f => !FileSorter.ShouldIgnore(f))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // --- Windows Installer (msi.dll) ---

    private static string? ReadMsiProperty(string path, string property)
    {
        if (MsiOpenDatabase(path, IntPtr.Zero, out var db) != 0)
            return null;
        try
        {
            if (MsiDatabaseOpenView(db, $"SELECT `Value` FROM `Property` WHERE `Property`='{property}'", out var view) != 0)
                return null;
            try
            {
                if (MsiViewExecute(view, IntPtr.Zero) != 0 || MsiViewFetch(view, out var record) != 0)
                    return null;
                try
                {
                    var size = 512;
                    var buffer = new StringBuilder(size);
                    return MsiRecordGetString(record, 1, buffer, ref size) == 0 ? buffer.ToString() : null;
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }
            finally
            {
                MsiCloseHandle(view);
            }
        }
        finally
        {
            MsiCloseHandle(db);
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr handle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(IntPtr db, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiRecordGetStringW")]
    private static extern uint MsiRecordGetString(IntPtr record, int field, StringBuilder value, ref int size);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
