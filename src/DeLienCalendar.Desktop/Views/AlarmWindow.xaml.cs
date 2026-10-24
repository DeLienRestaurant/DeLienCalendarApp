using System.Windows;
using System.Windows.Threading;
using DeLienCalendar.Desktop.Services;

namespace DeLienCalendar.Desktop.Views;

/// <summary>アラーム表示ウィンドウ（音声をループ再生し、停止・スヌーズできる）</summary>
public partial class AlarmWindow : Window
{
    private static readonly TimeSpan SnoozeInterval = TimeSpan.FromMinutes(5);

    private readonly AlarmPlayer _player = new();
    private readonly string? _soundPath;
    private readonly string? _deviceId;
    private DispatcherTimer? _snoozeTimer;

    public AlarmWindow(string title, string detail, string? soundPath, string? deviceId)
    {
        InitializeComponent();

        TitleText.Text = title;
        DetailText.Text = detail;
        _soundPath = soundPath;
        _deviceId = deviceId;

        if (_soundPath is null)
        {
            SnoozeButton.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => StartPlayback();
        Activated += (_, _) => Activate();
    }

    private void StartPlayback()
    {
        if (_soundPath is null) return;

        _ = _player.PlayAsync(_soundPath, _deviceId, loop: true)
            .ContinueWith(t =>
            {
                if (t.Exception is not null)
                {
                    AppLog.Write("アラーム音の再生に失敗しました", t.Exception.GetBaseException());
                }
            }, TaskScheduler.Default);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _snoozeTimer?.Stop();
        _player.Stop();
        Close();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();

        // ウィンドウを隠して 5 分後に再度鳴らす（プロセスは維持する）
        Hide();
        _snoozeTimer?.Stop();
        _snoozeTimer = new DispatcherTimer { Interval = SnoozeInterval };
        _snoozeTimer.Tick += (_, _) =>
        {
            _snoozeTimer?.Stop();
            Show();
            Activate();
            StartPlayback();
        };
        _snoozeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _snoozeTimer?.Stop();
        _player.Dispose();
        base.OnClosed(e);
    }
}
