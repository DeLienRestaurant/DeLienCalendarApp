using System.IO;
using System.Text.Json;

namespace DeLienCalendar.Desktop.Services;

/// <summary>アプリのローカル設定（端末単位）。%LOCALAPPDATA%\DeLienCalendar\settings.json に保存する。</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>表示テーマ（system / light / dark）</summary>
    public string AppTheme { get; set; } = ThemeService.SystemMode;

    /// <summary>接続先（既定: 本番）</summary>
    public string BaseUrl { get; set; } = "https://calendar.delien.xyz";

    /// <summary>Windows へのサインイン時に自動起動する</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>閉じるボタンでタスクトレイに格納する</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>通知を有効にする</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>リマインド時刻（開始何分前）。既定は現行 Web 版と同じ15分。</summary>
    public int ReminderMinutes { get; set; } = 15;

    /// <summary>通知スタイル: toast（標準トースト）/ alarm（ループ音アラーム）/ silent（無音）</summary>
    public string NotificationStyle { get; set; } = NotificationStyles.Toast;

    /// <summary>通知音（<c>builtin:Alarm01</c> または <c>custom:C:\path\sound.wav</c>）</summary>
    public string SoundFile { get; set; } = SoundCatalog.DefaultSound;

    /// <summary>再生に使う出力デバイス ID（空文字は Windows の既定デバイス）</summary>
    public string OutputDeviceId { get; set; } = AudioDeviceService.SystemDefault;

    /// <summary>起動時に更新を確認する</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>前回の更新確認時刻</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>通知本文にカレンダー名を含める</summary>
    public bool ShowCalendarName { get; set; } = true;

    /// <summary>通知本文に予定の説明を含める</summary>
    public bool ShowDescription { get; set; } = true;

    /// <summary>通知に「開く」「スヌーズ」ボタンを表示する</summary>
    public bool ShowActions { get; set; } = true;

    /// <summary>旧設定（互換用）。読み込み時に NotificationStyle へ移行する。</summary>
    public bool AlarmEnabled { get; set; }

    /// <summary>通知対象のカレンダーID。空の場合は関係する全カレンダー。</summary>
    public List<int> NotifyCalendarIds { get; set; } = new();

    /// <summary>ログイン中の Discord 表示名（表示用）</summary>
    public string? UserName { get; set; }

    /// <summary>前回の同期時刻</summary>
    public DateTime? LastSyncedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null) return loaded.Normalize();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("設定の読み込みに失敗したため既定値を使用します", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Write("設定の保存に失敗しました", ex);
        }
    }

    private AppSettings Normalize()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) BaseUrl = "https://calendar.delien.xyz";
        BaseUrl = BaseUrl.TrimEnd('/');
        if (ReminderMinutes < 0) ReminderMinutes = 0;
        if (ReminderMinutes > 1440) ReminderMinutes = 1440;
        NotifyCalendarIds ??= new List<int>();

        // 旧設定（AlarmEnabled のみ）からの移行
        if (!NotificationStyles.All.Contains(NotificationStyle ?? ""))
        {
            NotificationStyle = AlarmEnabled ? NotificationStyles.Alarm : NotificationStyles.Toast;
        }
        SoundFile = SoundCatalog.Normalize(SoundFile);
        OutputDeviceId ??= AudioDeviceService.SystemDefault;
        if (!ThemeService.All.Contains(AppTheme)) AppTheme = ThemeService.SystemMode;
        return this;
    }
}

/// <summary>通知スタイルの定義</summary>
public static class NotificationStyles
{
    public const string Toast = "toast";
    public const string Alarm = "alarm";
    public const string Silent = "silent";

    public static readonly string[] All = { Toast, Alarm, Silent };

    public static string Label(string style) => style switch
    {
        Alarm => "アラーム（ループ音・止めるまで鳴る）",
        Silent => "サイレント（音なし）",
        _ => "トースト（標準の通知音）",
    };
}

/// <summary>アプリのデータ保存先</summary>
public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeLienCalendar");
}

/// <summary>ログ出力（%LOCALAPPDATA%\DeLienCalendar\app.log）</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(AppPaths.DataDirectory, "app.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex is not null) line += Environment.NewLine + "  " + ex;
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // ログ失敗は無視する
        }
    }
}
