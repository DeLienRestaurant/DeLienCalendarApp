using System.Windows;
using System.Windows.Threading;
using DeLienCalendar.Desktop.Services;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DeLienCalendar.Desktop;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
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

        // タスクスケジューラから起動された「アラーム表示のみ」モード
        if (TryHandleNotifyMode(e.Args))
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

        // 2つ目の起動は既存ウィンドウを表示して終了する
        if (!SingleInstance.TryAcquire(out _singleInstance) || _singleInstance is null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();

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

    /// <summary>--notify {eventId} で起動された場合はトーストだけ表示して終了する</summary>
    private static bool TryHandleNotifyMode(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, "--notify", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        var settings = AppSettings.Load();
        if (!settings.NotificationsEnabled) return true;

        var cache = EventCache.Load();
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out var eventId))
        {
            var ev = cache.Events.FirstOrDefault(x => x.Id == eventId);
            if (ev is not null)
            {
                NotificationService.ShowEventReminder(ev, settings);
                return true;
            }
        }

        NotificationService.Show("DeLien Calendar", "予定の時刻になりました。", 0, settings);
        return true;
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
        if (_sync is null || _api is null) return;

        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _api, _sync) { Owner = _mainWindow };
        _settingsWindow.SignedOut += () =>
        {
            _sync.Stop();
            _settingsWindow = null;
            _mainWindow?.RequestReLogin();
            ShowMainWindow();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowDialog();
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
