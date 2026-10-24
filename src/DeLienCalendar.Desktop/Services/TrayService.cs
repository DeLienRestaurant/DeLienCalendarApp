using System.Drawing;
using System.Windows.Forms;

namespace DeLienCalendar.Desktop.Services;

/// <summary>タスクトレイ常駐アイコン</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _autoStartItem;

    public TrayService(bool autoStartEnabled)
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("カレンダーを開く", null, (_, _) => OpenRequested?.Invoke());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        _autoStartItem = new ToolStripMenuItem("Windows 起動時に自動起動", null, (_, _) => AutoStartToggled?.Invoke(!StartupService.IsEnabled()))
        {
            CheckOnClick = false,
            Checked = autoStartEnabled,
        };

        var reLoginItem = new ToolStripMenuItem("再ログイン", null, (_, _) => ReLoginRequested?.Invoke());
        var settingsItem = new ToolStripMenuItem("設定…", null, (_, _) => SettingsRequested?.Invoke());
        var updateItem = new ToolStripMenuItem("更新を確認", null, (_, _) => UpdateRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("終了", null, (_, _) => ExitRequested?.Invoke());

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(reLoginItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DeLien Calendar",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? UpdateRequested;
    public event Action? ReLoginRequested;
    public event Action? ExitRequested;
    public event Action<bool>? AutoStartToggled;

    public void RefreshAutoStart(bool enabled) => _autoStartItem.Checked = enabled;

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            AppLog.Write("バルーン通知の表示に失敗しました", ex);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico");
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null) return new Icon(stream);
        }
        catch (Exception ex)
        {
            AppLog.Write("アプリアイコンの読み込みに失敗しました", ex);
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
