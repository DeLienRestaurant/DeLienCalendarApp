using System.Windows;
using System.Windows.Threading;
using DeLienCalendar.Desktop.Models;
using DeLienCalendar.Desktop.Services;
using DeLienCalendar.Desktop.Views;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DeLienCalendar.Desktop;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private AppSettings _settings = new();
    private ApiClient? _api;
    private SyncService? _sync;
    private DispatcherTimer? _snoozeTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // アンインストーラからの後片付け（タスクスケジューラ登録・自動起動の解除）
        if (TryHandleUninstallCleanup(e.Args))
        {
            Shutdown();
            return;
        }

        // タスクスケジューラから起動された「ヘッドレス同期」モード
        if (TryHandleSyncMode(e.Args))
        {
            Shutdown();
            return;
        }

        // タスクスケジューラから起動された「アラーム表示のみ」モード
        if (HandleNotifyMode(e.Args) == NotifyMode.Handled)
        {
            Shutdown();
            return;
        }

        // 2つ目の起動は既存ウィンドウを表示して終了する
        if (!SingleInstance.TryAcquire(out _singleInstance) || _singleInstance is null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();

        // ウィンドウを作る前にテーマを適用する
        ThemeService.Apply(_settings.AppTheme);

        // 設定と実際の自動起動登録を一致させる
        if (_settings.StartWithWindows != StartupService.IsEnabled())
        {
            StartupService.SetEnabled(_settings.StartWithWindows);
        }

        _api = new ApiClient(_settings.BaseUrl) { Token = SecureStore.LoadToken() };
        _sync = new SyncService(_settings, _api);

        _tray = new TrayService(StartupService.IsEnabled());
        _tray.OpenRequested += ShowMainWindow;
        _tray.SettingsRequested += ShowSettings;
        _tray.UpdateRequested += OnUpdateRequested;
        _tray.ReLoginRequested += OnReLoginRequested;
        _tray.ExitRequested += ExitApp;
        _tray.AutoStartToggled += OnAutoStartToggled;

        _mainWindow = new MainWindow(_settings, _api, _sync);
        _mainWindow.LoginStateChanged += OnLoginStateChanged;
        _singleInstance.StartListening(ShowMainWindow);

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _ = InitializeSessionAsync();

        // 自動起動時（--tray）はウィンドウを出さずタスクトレイに常駐する
        var startMinimized = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            ShowMainWindow();
        }

        // 起動時に更新を確認する（設定が有効なら 24 時間に 1 回）
        _ = CheckUpdatesAsync(force: false);
    }

    /// <summary>保存済みの端末トークンを検証し、ログイン状態の確定と同期開始を行う</summary>
    private async Task InitializeSessionAsync()
    {
        if (_api is null || _mainWindow is null) return;

        if (string.IsNullOrEmpty(_api.Token))
        {
            _mainWindow.SetSessionActive(false, "この端末を Discord アカウントに連携します。");
            return;
        }

        try
        {
            var me = await _api.GetMeAsync();
            _settings.UserName = me.User?.DiscordName;
            _settings.Save();
            _mainWindow.SetSessionActive(true);
            ReminderScheduler.EnsureSyncTask();
            _sync?.Start();
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            // Web 側でトークンが失効している
            SecureStore.ClearToken();
            _api.Token = null;
            EventCache.Save(new CachedState());
            ReminderScheduler.RemoveOwnedTasks();
            ReminderScheduler.RemoveSyncTask();
            _mainWindow.SetSessionActive(false, "連携が解除されています。もう一度ログインしてください。");
        }
        catch (Exception ex)
        {
            // 通信できない場合も Web を表示する（ブラウザ版と同じ挙動）
            AppLog.Write("セッション確認に失敗しました", ex);
            _mainWindow.SetSessionActive(true);
            _sync?.Start();
        }
    }

    /// <summary>--uninstall-cleanup で起動された場合は登録を解除して終了する（アンインストーラから呼ばれる）</summary>
    private static bool TryHandleUninstallCleanup(string[] args)
    {
        if (!args.Any(a => string.Equals(a, "--uninstall-cleanup", StringComparison.OrdinalIgnoreCase))) return false;

        try
        {
            ReminderScheduler.RemoveOwnedTasks();
            StartupService.SetEnabled(false);
            AppLog.Write("アンインストール前の後片付けを実行しました（タスクと自動起動を解除）。");
        }
        catch (Exception ex)
        {
            AppLog.Write("アンインストール前の後片付けに失敗しました", ex);
        }
        return true;
    }

    /// <summary>--notify {eventId} で起動された場合のモード</summary>
    private enum NotifyMode
    {
        /// <summary>アラームモードではない</summary>
        None,
        /// <summary>通知を出し終えた（すぐ終了してよい）</summary>
        Handled,
    }

    /// <summary>
    /// --notify {eventId} で起動された場合はトーストとアラーム画面を出して終了する。
    /// トーストは無音で表示し、音は出力デバイスを指定してアプリ側で再生する。
    /// </summary>
    private NotifyMode HandleNotifyMode(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, "--notify", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return NotifyMode.None;

        var settings = AppSettings.Load();
        if (!settings.NotificationsEnabled) return NotifyMode.Handled;

        ApiEvent? calendarEvent = null;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out var eventId))
        {
            calendarEvent = EventCache.Load().Events.FirstOrDefault(x => x.Id == eventId);
        }

        if (calendarEvent is not null)
        {
            NotificationService.ShowEventReminder(calendarEvent, settings);
        }
        else
        {
            NotificationService.Show("DeLien Calendar", "予定の時刻になりました。", 0, settings);
        }

        if (settings.NotificationStyle == NotificationStyles.Silent) return NotifyMode.Handled;

        var soundPath = SoundCatalog.ResolvePath(settings.SoundFile);
        if (soundPath is null)
        {
            AppLog.Write($"通知音が見つかりません: {settings.SoundFile}");
            return NotifyMode.Handled;
        }

        // 音を鳴らすときは必ずアラーム画面（停止・スヌーズ）を出す。
        // アラームは止めるまでループし、トーストは1回鳴らして鳴り終わったら閉じる。
        var window = new AlarmWindow(
            calendarEvent?.Title ?? "DeLien Calendar",
            calendarEvent is null ? "予定の時刻になりました。" : BuildAlarmDetail(calendarEvent),
            soundPath,
            settings.OutputDeviceId,
            loop: settings.NotificationStyle == NotificationStyles.Alarm);
        window.ShowDialog();
        return NotifyMode.Handled;
    }

    private static string BuildAlarmDetail(ApiEvent? calendarEvent)
    {
        if (calendarEvent is null) return "";
        var time = calendarEvent.IsAllDay
            ? $"{calendarEvent.StartLocal:M月d日(ddd)} 終日"
            : $"{calendarEvent.StartLocal:M月d日(ddd) HH:mm} - {calendarEvent.EndLocal:HH:mm}";
        return string.IsNullOrWhiteSpace(calendarEvent.CalendarName) ? time : $"{time} / {calendarEvent.CalendarName}";
    }

    /// <summary>--sync で起動された場合はヘッドレス同期だけ行って終了する</summary>
    private static bool TryHandleSyncMode(string[] args)
    {
        if (!args.Any(a => string.Equals(a, "--sync", StringComparison.OrdinalIgnoreCase))) return false;

        // 常駐アプリが動作中なら、そちらが定期的に同期しているので何もしない
        if (SingleInstance.IsRunning()) return true;

        var settings = AppSettings.Load();
        if (!settings.NotificationsEnabled)
        {
            // 通知が無効ならアラーム再登録は不要（既存タスクは解除済みのはず）
            return true;
        }

        var token = SecureStore.LoadToken();
        if (string.IsNullOrEmpty(token)) return true;

        try
        {
            var api = new ApiClient(settings.BaseUrl) { Token = token };
            using var sync = new SyncService(settings, api);
            Task.Run(() => sync.SyncAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Write("バックグラウンド同期に失敗しました", ex);
        }
        return true;
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            var argument = args.Argument ?? "";
            if (argument.StartsWith("snooze", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleSnooze(argument);
                return;
            }
            ShowMainWindow();
        });
    }

    private void ScheduleSnooze(string argument)
    {
        var eventId = 0;
        foreach (var pair in argument.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "event") int.TryParse(kv[1], out eventId);
        }

        _snoozeTimer?.Stop();
        _snoozeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _snoozeTimer.Tick += (_, _) =>
        {
            _snoozeTimer?.Stop();
            var settings = AppSettings.Load();
            var ev = EventCache.Load().Events.FirstOrDefault(x => x.Id == eventId);
            if (ev is not null) NotificationService.ShowEventReminder(ev, settings);
        };
        _snoozeTimer.Start();
        AppLog.Write($"スヌーズを登録しました（5分後 / event={eventId}）");
    }

    private void ShowMainWindow()
    {
        _mainWindow?.ShowFromTray();
    }

    private void ShowSettings()
    {
        // 設定はメインウィンドウのタブとして開く
        _mainWindow?.OpenSettingsTab();
        ShowMainWindow();
    }

    private void OnReLoginRequested()
    {
        _mainWindow?.RequestReLogin();
        ShowMainWindow();
    }

    private void OnLoginStateChanged(string? userName)
    {
        _settings.UserName = userName;
        _settings.Save();

        if (string.IsNullOrEmpty(userName))
        {
            _sync?.Stop();
            ReminderScheduler.RemoveOwnedTasks();
            ReminderScheduler.RemoveSyncTask();
            return;
        }

        ReminderScheduler.EnsureSyncTask();
        _sync?.Start();
        _tray?.ShowBalloon("DeLien Calendar", $"{userName} としてログインしました。通知を開始します。");
    }

    private void OnAutoStartToggled(bool enabled)
    {
        _settings.StartWithWindows = enabled;
        _settings.Save();
        StartupService.SetEnabled(enabled);
        _tray?.RefreshAutoStart(enabled);
    }

    /// <summary>更新確認（起動時は 24 時間に 1 回、手動実行時は強制）</summary>
    private async Task CheckUpdatesAsync(bool force)
    {
        var hasUpdate = await UpdateService.CheckAsync(_settings, force);
        var info = UpdateService.LatestAvailable;

        if (hasUpdate && info is not null)
        {
            _tray?.ShowBalloon("DeLien Calendar",
                $"新しいバージョン v{info.Version} があります。「設定」タブから確認できます。");
            return;
        }

        if (force)
        {
            var message = UpdateService.LastError is { } error
                ? $"更新を確認できませんでした: {error}"
                : "最新バージョンです。";
            _tray?.ShowBalloon("DeLien Calendar", message);
        }
    }

    private async void OnUpdateRequested()
    {
        await CheckUpdatesAsync(force: true);
    }

    private void ExitApp()
    {
        _settings.Save();
        _sync?.Stop();
        _mainWindow?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _snoozeTimer?.Stop();
        _sync?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        ToastNotificationManagerCompat.Uninstall();
        base.OnExit(e);
    }
}

