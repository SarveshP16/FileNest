using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileNest.Core.Services;

/// <summary>Small helper for reading and atomically writing JSON files under %APPDATA%\FileNest.</summary>
public static class JsonStore
{
    public static string DataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileNest");

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string fileName, Func<T> fallback)
    {
        var path = Path.Combine(DataFolder, fileName);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file shouldn't stop the app; keep a copy and start fresh.
            TryBackupCorrupt(path);
        }

        return fallback();
    }

    public static void Save<T>(string fileName, T value)
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, fileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
