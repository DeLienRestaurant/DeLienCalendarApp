using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace DeLienCalendar.Desktop.Services;

/// <summary>利用可能な更新</summary>
public sealed record UpdateInfo(string Version, string Tag, string ReleaseUrl, string? SetupDownloadUrl);

/// <summary>
/// GitHub Releases を使った更新確認。
/// 未署名配布のため自動インストールは行わず、新しいバージョンの案内のみ行う。
/// </summary>
public static class UpdateService
{
    /// <summary>リリースを公開しているリポジトリ</summary>
    private const string Repository = "DeLienRestaurant/DeLienCalendarApp";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>見つかった新しいバージョン（無ければ null）</summary>
    public static UpdateInfo? LatestAvailable { get; private set; }

    /// <summary>直近の失敗理由</summary>
    public static string? LastError { get; private set; }

    /// <summary>確認結果が変わったとき</summary>
    public static event Action? Changed;

    /// <summary>
    /// 最新リリースを確認する。
    /// <paramref name="force"/> が false の場合は設定（自動確認）と前回確認時刻を見てスキップする。
    /// </summary>
    public static async Task<bool> CheckAsync(AppSettings settings, bool force = false, CancellationToken ct = default)
    {
        if (!force)
        {
            if (!settings.AutoUpdateCheck) return false;
            if (settings.LastUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(24)) return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=20");
            request.Headers.UserAgent.ParseAdd("DeLienCalendar");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"HTTP {(int)response.StatusCode}";
                AppLog.Write("更新確認に失敗しました: " + LastError);
                Changed?.Invoke();
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                LastError = "リリース一覧を取得できませんでした。";
                Changed?.Invoke();
                return false;
            }

            // 下書きを除き、安定版を優先して最新を選ぶ
            var candidates = new List<(bool IsPrerelease, Version Version, UpdateInfo Info)>();
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;

                var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
                var tag = release.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
                var releaseUrl = release.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? "" : "";

                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) continue;

                string? setupUrl = null;
                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) &&
                            asset.TryGetProperty("browser_download_url", out var d))
                        {
                            setupUrl = d.GetString();
                        }
                    }
                }

                var versionText = tag.TrimStart('v', 'V');
                candidates.Add((isPrerelease, version, new UpdateInfo(versionText, tag, releaseUrl, setupUrl)));
            }

            var chosen = candidates
                .OrderBy(c => c.IsPrerelease ? 1 : 0)
                .ThenByDescending(c => c.Version)
                .FirstOrDefault();

            LastError = null;
            LatestAvailable = chosen.Info is not null && chosen.Version > CurrentVersion ? chosen.Info : null;

            settings.LastUpdateCheck = DateTime.Now;
            settings.Save();

            AppLog.Write(LatestAvailable is null
                ? $"更新確認: 最新です（最新リリース {chosen.Info?.Version ?? "なし"}）"
                : $"更新確認: 新しいバージョン v{LatestAvailable.Version} があります（現在 v{CurrentVersion.ToString(3)}）");

            Changed?.Invoke();
            return LatestAvailable is not null;
        }
        catch (OperationCanceledException)
        {
            LastError = "タイムアウトしました。";
            AppLog.Write("更新確認がタイムアウトしました。");
            Changed?.Invoke();
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Write("更新確認に失敗しました", ex);
            Changed?.Invoke();
            return false;
        }
    }

    public static void OpenReleasePage()
    {
        var url = LatestAvailable?.ReleaseUrl;
        if (string.IsNullOrEmpty(url)) url = $"https://github.com/{Repository}/releases/latest";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("リリースページを開けませんでした: " + url, ex);
        }
    }
}
