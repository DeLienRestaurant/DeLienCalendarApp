using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeLienCalendar.Desktop.Models;
using DeLienCalendar.Desktop.Services;

namespace DeLienCalendar.Desktop.Views;

/// <summary>アプリ内タブで表示する端末設定画面</summary>
public partial class SettingsView : UserControl
{
    private const string CustomSoundLabel = "カスタムファイル…";

    private static readonly (string Label, int Minutes)[] ReminderChoices =
    {
        ("通知しない (0分前)", -1),
        ("時刻ちょうど", 0),
        ("5分前", 5),
        ("10分前", 10),
        ("15分前 (既定)", 15),
        ("30分前", 30),
        ("1時間前", 60),
        ("1日前", 1440),
    };

    private readonly List<(int CalendarId, CheckBox Box)> _calendarBoxes = new();
    private readonly AlarmPlayer _previewPlayer = new();
    private List<AudioDevice> _devices = new();

    private AppSettings? _settings;
    private ApiClient? _api;
    private SyncService? _sync;
    private bool _initialized;
    private bool _applyingTheme;

    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>連携解除されたとき</summary>
    public event Action? SignedOut;

    /// <summary>画面を初期化する（メインウィンドウから一度だけ呼ぶ）</summary>
    public void Initialize(AppSettings settings, ApiClient api, SyncService sync)
    {
        if (_initialized) return;
        _initialized = true;

        _settings = settings;
        _api = api;
        _sync = sync;

        foreach (var choice in ReminderChoices)
        {
            ReminderMinutes.Items.Add(choice.Label);
        }

        foreach (var mode in ThemeService.All)
        {
            ThemeCombo.Items.Add(ThemeService.Label(mode));
        }

        foreach (var style in NotificationStyles.All)
        {
            StyleCombo.Items.Add(NotificationStyles.Label(style));
        }

        foreach (var sound in SoundCatalog.Items)
        {
            SoundCombo.Items.Add(sound.Label);
        }
        SoundCombo.Items.Add(CustomSoundLabel);

        _sync.Updated += _ => Dispatcher.Invoke(() =>
        {
            BuildCalendarList();
            UpdateStatus();
        });
        _sync.Failed += message => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "同期に失敗しました: " + message;
        });
        UpdateService.Changed += () => Dispatcher.Invoke(UpdateUpdateStatus);
        ThemeService.Changed += () => Dispatcher.Invoke(() =>
        {
            // コードで生成した要素は自動で色が変わらないため作り直す
            BuildChangelog();
            BuildCalendarList();
        });

        Refresh();
    }

    /// <summary>設定値と一覧を読み直す（タブ表示時に呼ぶ）</summary>
    public void Refresh()
    {
        if (_settings is null) return;

        NotificationsEnabled.IsChecked = _settings.NotificationsEnabled;
        StartWithWindows.IsChecked = StartupService.IsEnabled();
        CloseToTray.IsChecked = _settings.CloseToTray;
        ShowCalendarName.IsChecked = _settings.ShowCalendarName;
        ShowDescription.IsChecked = _settings.ShowDescription;
        ShowActions.IsChecked = _settings.ShowActions;
        AutoUpdateCheck.IsChecked = _settings.AutoUpdateCheck;

        _applyingTheme = true;
        var themeIndex = Array.IndexOf(ThemeService.All, _settings.AppTheme);
        ThemeCombo.SelectedIndex = themeIndex >= 0 ? themeIndex : 0;
        _applyingTheme = false;
        BaseUrlBox.Text = _settings.BaseUrl;

        var styleIndex = Array.IndexOf(NotificationStyles.All, _settings.NotificationStyle);
        StyleCombo.SelectedIndex = styleIndex >= 0 ? styleIndex : 0;
        SoundCombo.IsEnabled = _settings.NotificationStyle != NotificationStyles.Silent;

        var reminderIndex = Array.FindIndex(ReminderChoices, c => c.Minutes == _settings.ReminderMinutes);
        ReminderMinutes.SelectedIndex = reminderIndex >= 0 ? reminderIndex : 4;

        ApplySoundToUi(_settings.SoundFile);
        LoadDevices(_settings.OutputDeviceId);

        CurrentVersionText.Text = "v" + UpdateService.CurrentVersion.ToString(3);

        BuildCalendarList();
        BuildChangelog();
        UpdateStatus();
        UpdateUpdateStatus();
    }

    // ---------- ジャンル切り替え ----------

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelGeneral is null || sender is not RadioButton button) return;
        SelectCategory(button.Tag as string ?? "general");
    }

    private void SelectCategory(string category)
    {
        if (PanelGeneral is null) return;

        var panels = new Dictionary<string, UIElement>
        {
            ["general"] = PanelGeneral,
            ["appearance"] = PanelAppearance,
            ["notification"] = PanelNotification,
            ["sound"] = PanelSound,
            ["calendar"] = PanelCalendar,
            ["version"] = PanelVersion,
        };

        foreach (var (key, panel) in panels)
        {
            panel.Visibility = key == category ? Visibility.Visible : Visibility.Collapsed;
        }

        if (category == "calendar") BuildCalendarList();
        if (category == "version")
        {
            UpdateUpdateStatus();
            BuildChangelog();
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingTheme || _settings is null) return;

        var index = ThemeCombo.SelectedIndex;
        if (index < 0 || index >= ThemeService.All.Length) return;

        var mode = ThemeService.All[index];
        _settings.AppTheme = mode;
        _settings.Save();
        ThemeService.Apply(mode);
    }

    // ---------- 更新履歴 ----------

    /// <summary>テーマのリソースからブラシを取得する（テーマ切替時は作り直す）</summary>
    private static Brush ThemeBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private void BuildChangelog()
    {
        if (ChangelogPanel is null) return;
        ChangelogPanel.Children.Clear();

        var entries = ChangelogService.Load();
        if (entries.Count == 0)
        {
            ChangelogPanel.Children.Add(new TextBlock
            {
                Text = "更新履歴を読み込めませんでした（CHANGELOG.md が見つかりません）。",
                FontSize = 12,
                Foreground = ThemeBrush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var current = UpdateService.CurrentVersion;

        foreach (var entry in entries)
        {
            // アセンブリのバージョンは 4 桁（0.3.0.0）なので、主要3桁で比較する
            var isCurrent = Version.TryParse(entry.Version, out var version)
                && version.Major == current.Major
                && version.Minor == current.Minor
                && version.Build == current.Build;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
            header.Children.Add(new TextBlock
            {
                Text = "v" + entry.Version,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeBrush("AccentBrush"),
            });

            if (!string.IsNullOrEmpty(entry.Date))
            {
                header.Children.Add(new TextBlock
                {
                    Text = entry.Date,
                    FontSize = 12,
                    Foreground = ThemeBrush("MutedTextBrush"),
                    Margin = new Thickness(8, 3, 0, 0),
                });
            }

            if (isCurrent)
            {
                header.Children.Add(new Border
                {
                    Background = ThemeBrush("AccentSoftBrush"),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 1, 8, 2),
                    Margin = new Thickness(8, 1, 0, 0),
                    Child = new TextBlock { Text = "使用中", FontSize = 11, Foreground = ThemeBrush("AccentBrush") },
                });
            }

            ChangelogPanel.Children.Add(header);

            foreach (var item in entry.Items)
            {
                ChangelogPanel.Children.Add(new TextBlock
                {
                    Text = "・" + item,
                    FontSize = 13,
                    Foreground = ThemeBrush("TextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 1, 0, 1),
                });
            }
        }
    }

    // ---------- 音声 ----------

    private void ApplySoundToUi(string soundFile)
    {
        if (soundFile.StartsWith(SoundCatalog.CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            SoundCombo.SelectedIndex = SoundCombo.Items.Count - 1;
            CustomSoundBox.Text = soundFile[SoundCatalog.CustomPrefix.Length..];
            CustomSoundPanel.Visibility = Visibility.Visible;
            return;
        }

        var id = soundFile.StartsWith(SoundCatalog.BuiltinPrefix, StringComparison.OrdinalIgnoreCase)
            ? soundFile[SoundCatalog.BuiltinPrefix.Length..]
            : "";

        var index = -1;
        for (var i = 0; i < SoundCatalog.Items.Count; i++)
        {
            if (string.Equals(SoundCatalog.Items[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        SoundCombo.SelectedIndex = index >= 0 ? index : 0;
        CustomSoundPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>画面上の選択から音声の設定値を組み立てる</summary>
    private string CurrentSoundFile()
    {
        if (SoundCombo.SelectedIndex >= 0 && SoundCombo.SelectedIndex == SoundCombo.Items.Count - 1)
        {
            var path = CustomSoundBox.Text.Trim();
            return path.Length > 0 ? SoundCatalog.CustomPrefix + path : SoundCatalog.DefaultSound;
        }

        var index = SoundCombo.SelectedIndex >= 0 ? SoundCombo.SelectedIndex : 0;
        return SoundCatalog.BuiltinPrefix + SoundCatalog.Items[index].Id;
    }

    private void SoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSoundPanel is null) return;
        var isCustom = SoundCombo.SelectedIndex >= 0 && SoundCombo.SelectedIndex == SoundCombo.Items.Count - 1;
        CustomSoundPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoundCombo is null) return;
        var style = StyleCombo.SelectedIndex >= 0
            ? NotificationStyles.All[StyleCombo.SelectedIndex]
            : NotificationStyles.Toast;
        var enabled = style != NotificationStyles.Silent;
        SoundCombo.IsEnabled = enabled;
        PreviewButton.IsEnabled = enabled;
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "アラーム音を選択",
            Filter = "音声ファイル|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac|すべてのファイル|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            SoundCombo.SelectedIndex = SoundCombo.Items.Count - 1;
            CustomSoundBox.Text = dialog.FileName;
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var path = SoundCatalog.ResolvePath(CurrentSoundFile());
        if (path is null)
        {
            StatusText.Text = "音声ファイルが見つかりません。";
            return;
        }

        var deviceId = SelectedDeviceId();
        _previewPlayer.Stop();
        _ = _previewPlayer.PlayAsync(path, deviceId, loop: false, maxDuration: TimeSpan.FromSeconds(10))
            .ContinueWith(t =>
            {
                if (t.Exception is not null)
                {
                    Dispatcher.Invoke(() => StatusText.Text = "音声を再生できませんでした: " + t.Exception.GetBaseException().Message);
                }
            }, TaskScheduler.Default);
    }

    private void LoadDevices(string selectedId)
    {
        _devices = AudioDeviceService.GetOutputDevices();
        DeviceCombo.Items.Clear();
        foreach (var device in _devices)
        {
            DeviceCombo.Items.Add(device.Name);
        }

        var index = _devices.FindIndex(d => string.Equals(d.Id, selectedId ?? "", StringComparison.Ordinal));
        DeviceCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    private string SelectedDeviceId() =>
        DeviceCombo.SelectedIndex >= 0 && DeviceCombo.SelectedIndex < _devices.Count
            ? _devices[DeviceCombo.SelectedIndex].Id
            : AudioDeviceService.SystemDefault;

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => LoadDevices(SelectedDeviceId());

    // ---------- 更新 ----------

    private void UpdateUpdateStatus()
    {
        var current = UpdateService.CurrentVersion.ToString(3);
        var info = UpdateService.LatestAvailable;

        if (info is not null)
        {
            var prerelease = info.IsPrerelease ? "（プレリリース）" : "";
            UpdateStatusText.Text = $"現在 v{current} → 新しいバージョン v{info.Version}{prerelease} があります。";
            OpenReleaseButton.Visibility = Visibility.Visible;
            ApplyUpdateButton.Visibility = info.SetupDownloadUrl is not null ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        OpenReleaseButton.Visibility = Visibility.Collapsed;
        ApplyUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = UpdateService.LastError is { } error
            ? $"現在 v{current}（確認できませんでした: {error}）"
            : $"現在 v{current}（最新です）";
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        var info = UpdateService.LatestAvailable;
        if (_settings is null || info is null) return;

        var answer = MessageBox.Show(
            $"v{info.Version} にアップデートします。\n\n" +
            "アプリを終了してインストーラを起動し、完了後にアプリを起動し直します。\n" +
            "設定（通知・音声など）は引き継がれます。\n\n" +
            "※ コード署名を行っていないため、Windows の警告が表示される場合があります。\n" +
            "　その場合は「詳細情報」→「実行」を選んでください。\n\n続行しますか？",
            "アップデートの確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        ApplyUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;

        try
        {
            var progress = new Progress<int>(percent =>
                UpdateStatusText.Text = $"ダウンロード中… {percent}%");

            var installerPath = await UpdateService.DownloadInstallerAsync(info, progress);
            UpdateStatusText.Text = "インストーラを起動します。アプリを終了します…";

            _settings.Save();
            UpdateService.LaunchInstaller(installerPath);

            // ファイルロックを解放し、インストーラに置き換えてもらう
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Write("アプリ内アップデートに失敗しました", ex);
            UpdateStatusText.Text = "アップデートできませんでした: " + ex.Message;
            ApplyUpdateButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;

        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "確認しています…";
        try
        {
            await UpdateService.CheckAsync(_settings, force: true);
            UpdateUpdateStatus();
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e) => UpdateService.OpenReleasePage();

    // ---------- カレンダー / 状態 ----------

    private void BuildCalendarList()
    {
        if (_sync is null || _settings is null) return;

        var state = _sync.State;
        CalendarList.Children.Clear();
        _calendarBoxes.Clear();

        if (state.Calendars.Count == 0)
        {
            CalendarList.Children.Add(new TextBlock
            {
                Text = "カレンダー情報がまだ取得できていません。「今すぐ同期」を実行してください。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryFindResource("MutedTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray,
            });
            return;
        }

        foreach (var calendar in state.Calendars)
        {
            var box = new CheckBox
            {
                Content = $"{calendar.Name}（{PermissionLabel(calendar.Permission)}）",
                IsChecked = _settings.NotifyCalendarIds.Count == 0 || _settings.NotifyCalendarIds.Contains(calendar.Id),
                Margin = new Thickness(0, 3, 0, 3),
            };
            _calendarBoxes.Add((calendar.Id, box));
            CalendarList.Children.Add(box);
        }
    }

    private static string PermissionLabel(string? permission) => permission switch
    {
        "owner" => "作成者",
        "admin" => "管理者",
        "editor" => "編集者",
        "member" => "メンバー",
        _ => "閲覧者",
    };

    private void UpdateStatus()
    {
        var version = typeof(SettingsView).Assembly.GetName().Version;
        var versionText = version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}　/　";
        var synced = _settings?.LastSyncedAt;
        StatusText.Text = versionText + (synced is null
            ? "最終同期: 未同期"
            : $"最終同期: {synced:yyyy/MM/dd HH:mm}");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        if (_settings is null || _sync is null) return;

        _settings.Save();
        StartupService.SetEnabled(_settings.StartWithWindows);

        // 通知対象が変わったのでアラームを登録し直す
        _sync.State.ScheduleHash = null;
        _ = _sync.SyncAsync();

        UpdateStatus();
        StatusText.Text += "　/　設定を保存しました。";
    }

    private void ApplyToSettings()
    {
        if (_settings is null) return;

        _settings.NotificationsEnabled = NotificationsEnabled.IsChecked == true;
        _settings.CloseToTray = CloseToTray.IsChecked == true;
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;
        _settings.ShowCalendarName = ShowCalendarName.IsChecked == true;
        _settings.ShowDescription = ShowDescription.IsChecked == true;
        _settings.ShowActions = ShowActions.IsChecked == true;
        _settings.AutoUpdateCheck = AutoUpdateCheck.IsChecked == true;

        var style = StyleCombo.SelectedIndex >= 0
            ? NotificationStyles.All[StyleCombo.SelectedIndex]
            : NotificationStyles.Toast;
        _settings.NotificationStyle = style;
        _settings.AlarmEnabled = style == NotificationStyles.Alarm;

        _settings.SoundFile = SoundCatalog.Normalize(CurrentSoundFile());
        _settings.OutputDeviceId = SelectedDeviceId();

        var choice = ReminderMinutes.SelectedIndex >= 0 ? ReminderChoices[ReminderMinutes.SelectedIndex] : ReminderChoices[4];
        _settings.NotificationsEnabled = _settings.NotificationsEnabled && choice.Minutes >= 0;
        _settings.ReminderMinutes = Math.Max(0, choice.Minutes);

        var url = BaseUrlBox.Text.Trim();
        if (url.Length > 0) _settings.BaseUrl = url.TrimEnd('/');

        var checkedIds = _calendarBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.CalendarId).ToList();
        _settings.NotifyCalendarIds = checkedIds.Count == _calendarBoxes.Count ? new List<int>() : checkedIds;
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sync is null) return;

        SyncButton.IsEnabled = false;
        StatusText.Text = "同期しています…";
        try
        {
            await _sync.SyncAsync();
            BuildCalendarList();
            UpdateStatus();
            StatusText.Text += $"　/　予定 {_sync.State.Events.Count} 件を取得しました。";
        }
        finally
        {
            SyncButton.IsEnabled = true;
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _api is null) return;

        var answer = MessageBox.Show(
            "この端末の連携を解除します。通知もすべて削除されます。よろしいですか？",
            "DeLien Calendar", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        SecureStore.ClearToken();
        _api.Token = null;
        _settings.UserName = null;
        _settings.LastSyncedAt = null;
        _settings.Save();
        ReminderScheduler.RemoveOwnedTasks();
        ReminderScheduler.RemoveSyncTask();
        EventCache.Save(new CachedState());
        Refresh();

        SignedOut?.Invoke();
    }
}
