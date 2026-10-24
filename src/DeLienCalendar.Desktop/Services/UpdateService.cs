using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace DeLienCalendar.Desktop.Services;

/// <summary>利用可能な更新</summary>
public sealed record UpdateInfo(
    string Version,
    string Tag,
    string ReleaseUrl,
    string? SetupDownloadUrl,
    string? ChecksumDownloadUrl,
    bool IsPrerelease);

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
                string? checksumUrl = null;
                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var url = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                        if (url is null) continue;

                        if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            setupUrl = url;
                        }
                        else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            checksumUrl = url;
                        }
                    }
                }

                var versionText = tag.TrimStart('v', 'V');
                candidates.Add((isPrerelease, version,
                    new UpdateInfo(versionText, tag, releaseUrl, setupUrl, checksumUrl, isPrerelease)));
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

    // ---------- アプリ内アップデート ----------

    /// <summary>ダウンロード専用（大きなファイル向けに長めのタイムアウト）</summary>
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// インストーラをダウンロードして検証する。戻り値はダウンロードしたファイルのパス。
    /// SHA256SUMS.txt が取得できた場合はハッシュを検証する。
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        UpdateInfo info, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.SetupDownloadUrl))
        {
            throw new InvalidOperationException(
                "このリリースにはインストーラが含まれていません。リリースページからダウンロードしてください。");
        }

        var directory = Path.Combine(AppPaths.DataDirectory, "updates");
        Directory.CreateDirectory(directory);

        // 以前のダウンロードを掃除する
        foreach (var old in Directory.EnumerateFiles(directory))
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                // 使用中などで消せない場合は無視する
            }
        }

        var fileName = $"DeLienCalendar-{info.Version}-setup.exe";
        var filePath = Path.Combine(directory, fileName);

        using (var response = await DownloadHttp
                   .GetAsync(info.SetupDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(filePath);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                if (total > 0) progress?.Report((int)(received * 100 / total));
            }
        }

        progress?.Report(100);

        var expected = await GetExpectedHashAsync(info.ChecksumDownloadUrl, fileName, ct).ConfigureAwait(false);
        if (expected is null)
        {
            AppLog.Write("チェックサムを取得できないため、ダウンロードの検証をスキップしました。");
            return filePath;
        }

        var actual = ComputeSha256(filePath);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // 削除失敗は無視する
            }
            throw new InvalidDataException("ダウンロードしたファイルの検証に失敗しました（破損または改ざんの可能性があります）。");
        }

        AppLog.Write($"インストーラをダウンロードしました（SHA-256 一致）: {filePath}");
        return filePath;
    }

    /// <summary>SHA256SUMS.txt から対象ファイルの期待ハッシュを取得する</summary>
    private static async Task<string?> GetExpectedHashAsync(string? checksumUrl, string fileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(checksumUrl)) return null;

        try
        {
            using var response = await DownloadHttp.GetAsync(checksumUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (string.Equals(parts[1].TrimStart('*'), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return parts[0];
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("チェックサムの取得に失敗しました（検証をスキップ）", ex);
        }

        return null;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// アプリを終了したあとにインストーラを実行し、完了後にアプリを起動し直す。
    /// 呼び出し側はこの直後に Shutdown() すること。
    /// </summary>
    public static void LaunchInstaller(string installerPath)
    {
        var command = BuildUpdateCommand(installerPath, ResolveInstallTargetExe());

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            AppLog.Write("アップデート用インストーラを起動しました: " + installerPath);
        }
        catch (Exception ex)
        {
            AppLog.Write("インストーラを起動できませんでした", ex);
            throw new InvalidOperationException("インストーラを起動できませんでした。", ex);
        }
    }

    /// <summary>
    /// インストーラ実行 → アプリ再起動を行う cmd.exe のコマンド文字列を組み立てる。
    /// 自プロセスの終了を待つため、最初に数秒待機する。
    /// </summary>
    public static string BuildUpdateCommand(string installerPath, string? appExe)
    {
        var command = $"ping -n 6 127.0.0.1 >nul & \"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        if (!string.IsNullOrEmpty(appExe))
        {
            command += $" & start \"\" \"{appExe}\"";
        }
        return command;
    }

    /// <summary>インストール先のアンインストール情報（Inno Setup の AppId）</summary>
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{4C0B7A62-9E15-4C8F-A3D6-7B2E5F91C4A8}_is1";

    /// <summary>
    /// 更新後に起動し直すアプリのパスを求める。
    /// インストーラ管理下ならそのインストール先、無ければインストーラの既定インストール先。
    /// （ポータブル版からアップデートした場合も、新しいインストール先で起動できる）
    /// </summary>
    public static string ResolveInstallTargetExe()
    {
        var views = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };

        foreach (var (hive, view) in views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(UninstallKeyPath);
                if (key is null) continue;

                if (key.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                {
                    return Path.Combine(location.TrimEnd('\\'), "DeLienCalendar.exe");
                }

                if (key.GetValue("UninstallString") is string uninstallString)
                {
                    var uninstaller = uninstallString.Trim().Trim('"');
                    var directory = Path.GetDirectoryName(uninstaller);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        return Path.Combine(directory, "DeLienCalendar.exe");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("インストール先の取得に失敗しました", ex);
            }
        }

        // 未インストール（ポータブル版）の場合はインストーラの既定インストール先
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "DeLienCalendar",
            "DeLienCalendar.exe");
    }
}
