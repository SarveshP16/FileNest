using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Windows.Threading;

namespace FileNest.Services;

/// <summary>
/// Anonymous usage counts sent to Aptabase (https://aptabase.com), so the developer can see how many people
/// use FileNest. Sends only event names, small numbers and system info (app version, Windows version,
/// language). Never file names, folders or any ID that follows a person between sessions.
/// Nothing is sent when <see cref="AppKey"/> is empty or the user turns usage stats off in Settings.
/// </summary>
public static class UsageStats
{
    /// <summary>The Aptabase App Key ("A-EU-…" or "A-US-…") from the Aptabase dashboard. Empty turns stats off.</summary>
    public const string AppKey = "";

    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(60);
    private static readonly Dictionary<string, string> Hosts = new()
    {
        ["EU"] = "https://eu.aptabase.com",
        ["US"] = "https://us.aptabase.com",
    };

    private static HttpClient? _http;
    private static Func<bool> _enabled = () => false;
    private static object? _system;
    private static string _session = NewSessionId();
    private static DateTime _lastTouched = DateTime.UtcNow;
    private static DateTime _lastActiveDay;
    private static DispatcherTimer? _dailyTimer;

    /// <param name="enabled">Read before every event, so the Settings switch applies immediately.</param>
    /// <param name="testRun">Marks events as debug so they stay out of the real numbers.</param>
    public static void Start(Func<bool> enabled, bool testRun)
    {
        _enabled = enabled;
        var parts = AppKey.Split('-');
        if (parts.Length != 3 || !Hosts.TryGetValue(parts[1], out var host))
            return;

        _http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("App-Key", AppKey);

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
#if DEBUG
        var debug = true;
#else
        var debug = testRun;
#endif
        _system = new
        {
            isDebug = debug,
            osName = "Windows",
            osVersion = Environment.OSVersion.Version.ToString(),
            locale = CultureInfo.CurrentUICulture.Name,
            appVersion = version,
            appBuildNumber = "",
            sdkVersion = $"FileNest@{version}",
            deviceModel = "",
        };

        // FileNest runs for days in the tray, so send one event per day it's running to count daily users.
        _lastActiveDay = DateTime.Today;
        _dailyTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _dailyTimer.Tick += (_, _) =>
        {
            if (DateTime.Today == _lastActiveDay)
                return;
            _lastActiveDay = DateTime.Today;
            Track("daily_active");
        };
        _dailyTimer.Start();
    }

    /// <summary>Sends one event in the background. Never throws and never blocks the UI.</summary>
    public static void Track(string eventName, Dictionary<string, object>? props = null)
    {
        if (_http is null || _system is null || !_enabled())
            return;

        var now = DateTime.UtcNow;
        if (now - _lastTouched >= SessionTimeout)
            _session = NewSessionId();
        _lastTouched = now;

        var body = new
        {
            timestamp = now.ToString("o"),
            sessionId = _session,
            eventName,
            systemProps = _system,
            props = props ?? [],
        };
        _ = SendAsync(body);
    }

    private static async Task SendAsync(object body)
    {
        try
        {
            using var response = await _http!.PostAsJsonAsync("/api/v0/event", body).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Offline or blocked: usage stats are best-effort.
        }
    }

    // Same format as Aptabase's own SDKs: seconds since 1970 followed by 8 random digits.
    private static string NewSessionId() =>
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 100_000_000 + Random.Shared.NextInt64(0, 99_999_999)).ToString(CultureInfo.InvariantCulture);
}
