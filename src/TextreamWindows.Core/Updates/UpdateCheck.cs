using System.Text.Json;

namespace TextreamWindows.Core.Updates;

public enum UpdateStatus
{
    UpdateAvailable,
    UpToDate,

    /// <summary>GitHub 的回應讀不懂（還沒有 Release、被限流、不是 JSON）。</summary>
    Unreadable,
}

/// <summary>一次檢查更新的結果。<see cref="ReleaseUrl"/> 是按「前往下載」要開的網頁。</summary>
public sealed record UpdateCheckResult(UpdateStatus Status, string? LatestVersion = null, string? ReleaseUrl = null);

/// <summary>
/// 檢查更新（瓦基 2026-10-08，照 Mac 版 textream-zh 的 UpdateChecker）：查 GitHub 這個 repo 的最新 Release，比目前版本新就問要不要去下載。
/// 這裡只放不用連網的部分，連網在 App。
/// </summary>
public static class UpdateCheck
{
    private const string Repository = "wakichuang/textream-windows";

    public const string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    public const string ReleasesPage = $"https://github.com/{Repository}/releases/latest";

    /// <summary>只開這個 repo 在 GitHub 上的頁面：回應被竄改也不會把使用者帶去別的網站。</summary>
    private const string AllowedPagePrefix = $"https://github.com/{Repository}/";

    /// <summary>逐段比數字（0.10.0 比 0.9.0 新），前面的 v 不算，少的段當 0。</summary>
    public static bool IsNewer(string remote, string local)
    {
        var r = Parts(remote);
        var l = Parts(local);
        for (var i = 0; i < Math.Max(r.Length, l.Length); i++)
        {
            var rv = i < r.Length ? r[i] : 0;
            var lv = i < l.Length ? l[i] : 0;
            if (rv != lv)
            {
                return rv > lv;
            }
        }
        return false;
    }

    /// <param name="currentVersion">這個程式的版本</param>
    /// <param name="latestReleaseJson">GitHub <see cref="LatestReleaseApi"/> 回的內容</param>
    public static UpdateCheckResult Evaluate(string currentVersion, string latestReleaseJson)
    {
        string? tag;
        string? url;
        try
        {
            using var document = JsonDocument.Parse(latestReleaseJson);
            var root = document.RootElement;
            tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        }
        catch (JsonException)
        {
            return new(UpdateStatus.Unreadable);
        }
        if (string.IsNullOrWhiteSpace(tag) || url is null)
        {
            return new(UpdateStatus.Unreadable);
        }

        var latest = StripPrefix(tag);
        var page = url.StartsWith(AllowedPagePrefix, StringComparison.Ordinal) ? url : ReleasesPage;
        return new(IsNewer(latest, currentVersion) ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate, latest, page);
    }

    private static string StripPrefix(string version) =>
        version.Trim().TrimStart('v', 'V');

    private static int[] Parts(string version) =>
        StripPrefix(version).Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
}
