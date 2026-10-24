using System.Windows;
using System.Windows.Controls;
using DeLienCalendar.Desktop.Models;
using DeLienCalendar.Desktop.Services;

namespace DeLienCalendar.Desktop;

/// <summary>端末単位の通知設定・ログイン状態を管理する画面</summary>
public partial class SettingsWindow : Window
{
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

    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly SyncService _sync;
    private readonly List<(int CalendarId, CheckBox Box)> _calendarBoxes = new();
    private int _selectedIndex;

    public SettingsWindow(AppSettings settings, ApiClient api, SyncService sync)
    {
        InitializeComponent();
        _settings = settings;
        _api = api;
        _sync = sync;

        foreach (var choice in ReminderChoices)
        {
            ReminderMinutes.Items.Add(choice.Label);
            if (choice.Minutes == settings.ReminderMinutes) _selectedIndex = ReminderMinutes.Items.Count - 1;
        }
        ReminderMinutes.SelectedIndex = _selectedIndex;
        NotificationsEnabled.IsChecked = settings.NotificationsEnabled;
        CloseToTray.IsChecked = settings.CloseToTray;

        foreach (var style in NotificationStyles.All)
        {
            StyleCombo.Items.Add(NotificationStyles.Label(style));
            if (style == settings.NotificationStyle) StyleCombo.SelectedIndex = StyleCombo.Items.Count - 1;
        }
        if (StyleCombo.SelectedIndex < 0) StyleCombo.SelectedIndex = 0;

        foreach (var sound in Sounds.All)
        {
            SoundCombo.Items.Add(Sounds.Label(sound));
            if (sound == settings.SoundName) SoundCombo.SelectedIndex = SoundCombo.Items.Count - 1;
        }
        if (SoundCombo.SelectedIndex < 0) SoundCombo.SelectedIndex = 0;

        ShowCalendarName.IsChecked = settings.ShowCalendarName;
        ShowDescription.IsChecked = settings.ShowDescription;
        ShowActions.IsChecked = settings.ShowActions;
        SoundCombo.IsEnabled = settings.NotificationStyle == NotificationStyles.Toast;

        StartWithWindows.IsChecked = StartupService.IsEnabled();

        BuildCalendarList(sync.State);
        UpdateStatus();
    }

    /// <summary>連携解除されたとき</summary>
    public event Action? SignedOut;

    private void BuildCalendarList(CachedState state)
    {
        CalendarList.Children.Clear();
        _calendarBoxes.Clear();

        if (state.Calendars.Count == 0)
        {
            CalendarList.Children.Add(new TextBlock
            {
                Text = "カレンダー情報がまだ取得できていません。「今すぐ同期」を実行してください。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
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
        var synced = _settings.LastSyncedAt;
        StatusText.Text = synced is null
            ? "最終同期: 未同期"
            : $"最終同期: {synced:yyyy/MM/dd HH:mm}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
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
        _settings.NotificationsEnabled = NotificationsEnabled.IsChecked == true;
        _settings.CloseToTray = CloseToTray.IsChecked == true;
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;

        _settings.NotificationStyle = StyleCombo.SelectedIndex >= 0
            ? NotificationStyles.All[StyleCombo.SelectedIndex]
            : NotificationStyles.Toast;
        _settings.SoundName = SoundCombo.SelectedIndex >= 0 ? Sounds.All[SoundCombo.SelectedIndex] : Sounds.Default;
        _settings.AlarmEnabled = _settings.NotificationStyle == NotificationStyles.Alarm;
        _settings.ShowCalendarName = ShowCalendarName.IsChecked == true;
        _settings.ShowDescription = ShowDescription.IsChecked == true;
        _settings.ShowActions = ShowActions.IsChecked == true;

        var choice = ReminderMinutes.SelectedIndex >= 0 ? ReminderChoices[ReminderMinutes.SelectedIndex] : ReminderChoices[4];
        _settings.NotificationsEnabled = _settings.NotificationsEnabled && choice.Minutes >= 0;
        _settings.ReminderMinutes = Math.Max(0, choice.Minutes);

        var checkedIds = _calendarBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.CalendarId).ToList();
        _settings.NotifyCalendarIds = checkedIds.Count == _calendarBoxes.Count ? new List<int>() : checkedIds;
    }

    private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoundCombo is null) return;
        var style = StyleCombo.SelectedIndex >= 0 ? NotificationStyles.All[StyleCombo.SelectedIndex] : NotificationStyles.Toast;
        SoundCombo.IsEnabled = style == NotificationStyles.Toast;
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        SyncButton.IsEnabled = false;
        StatusText.Text = "同期しています…";
        try
        {
            await _sync.SyncAsync();
            BuildCalendarList(_sync.State);
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

        SignedOut?.Invoke();
        Close();
    }
}
