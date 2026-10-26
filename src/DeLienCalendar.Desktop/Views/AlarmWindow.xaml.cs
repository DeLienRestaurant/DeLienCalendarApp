using System.Windows;
using System.Windows.Threading;
using DeLienCalendar.Desktop.Services;

namespace DeLienCalendar.Desktop.Views;

/// <summary>アラーム表示ウィンドウ（音声を再生し、停止・スヌーズできる）</summary>
public partial class AlarmWindow : Window
{
    private static readonly TimeSpan SnoozeInterval = TimeSpan.FromMinutes(5);

    private readonly AlarmPlayer _player = new();
    private readonly string _soundPath;
    private readonly string? _deviceId;
    private readonly bool _loop;
    private DispatcherTimer? _snoozeTimer;
    private bool _closed;

    /// <param name="loop">
    /// true なら停止するまで繰り返し再生する（アラーム）。
    /// false なら1回だけ再生し、鳴り終わったら自動で閉じる（トースト）。
    /// </param>
    public AlarmWindow(string title, string detail, string soundPath, string? deviceId, bool loop)
    {
        InitializeComponent();

        TitleText.Text = title;
        DetailText.Text = detail;
        _soundPath = soundPath;
        _deviceId = deviceId;
        _loop = loop;

        HintText.Text = loop
            ? "「停止」を押すまで音が鳴り続けます。"
            : "音が鳴り終わると自動で閉じます。";

        Loaded += (_, _) => StartPlayback();
        Activated += (_, _) => Activate();
    }

    private async void StartPlayback()
    {
        try
        {
            await _player.PlayAsync(_soundPath, _deviceId, _loop);
        }
        catch (Exception ex)
        {
            AppLog.Write("アラーム音の再生に失敗しました", ex);
            return;
        }

        // 1回だけ鳴らすモードでは、鳴り終わったら自動で閉じる
        if (!_loop && !_closed) Close();
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
        _closed = true;
        _snoozeTimer?.Stop();
        _player.Dispose();
        base.OnClosed(e);
    }
}