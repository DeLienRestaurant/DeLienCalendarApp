using System.Diagnostics;
using System.IO;
using System.Windows;
using DeLienCalendar.Desktop.Services;
using DeLienCalendar.Desktop.Views;
using Microsoft.Web.WebView2.Core;

namespace DeLienCalendar.Desktop;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly SyncService _sync;
    private readonly DesktopAuthService _auth;

    private SettingsView? _settingsView;
    private bool _webReady;
    private bool _forceClose;
    private bool _sessionActive;
    private string _loginMessage = "この端末を Discord アカウントに連携します。";

    public MainWindow(AppSettings settings, ApiClient api, SyncService sync)
    {
        _settings = settings;
        _api = api;
        _sync = sync;
        _auth = new DesktopAuthService(_api);

        InitializeComponent();

        // 設定タブの中身（アプリ内で完結する設定画面）
        _settingsView = new SettingsView();
        SettingsHost.Children.Add(_settingsView);
        _settingsView.Initialize(_settings, _api, _sync);
        _settingsView.SignedOut += OnSettingsSignedOut;

        Loaded += OnLoadedAsync;
        Closing += OnClosing;

        SelectTab(showSettings: false);
    }

    /// <summary>ログイン状態が変わったとき（Discord 表示名 / 未ログインは null）</summary>
    public event Action<string?>? LoginStateChanged;

    /// <summary>設定タブを開く（トレイメニューから呼ばれる）</summary>
    public void OpenSettingsTab()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OpenSettingsTab));
            return;
        }
        SettingsTab.IsChecked = true;
        SelectTab(showSettings: true);
    }

    public void ShowFromTray()
    {
        // 単一インスタンスの通知はバックグラウンドスレッドから来るため、UI スレッドへ委譲する
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ShowFromTray));
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>端末トークンを破棄してログイン画面に戻す</summary>
    public void RequestReLogin()
    {
        _sync.Stop();
        SecureStore.ClearToken();
        _api.Token = null;
        _settings.UserName = null;
        _settings.Save();
        try
        {
            Web.CoreWebView2?.Navigate("about:blank");
        }
        catch (Exception ex)
        {
            AppLog.Write("WebView2 のリセットに失敗しました", ex);
        }
        CalendarTab.IsChecked = true;
        LoginStateChanged?.Invoke(null);
        SetSessionActive(false, "端末の連携を解除しました。もう一度ログインしてください。");
    }

    /// <summary>
    /// ログイン状態を画面へ反映する。
    /// ウィンドウが未表示（タスクトレイ常駐）でも状態だけ保持し、表示時に適用する。
    /// </summary>
    public void SetSessionActive(bool active, string? message = null)
    {
        _sessionActive = active;
        if (message is not null) _loginMessage = message;
        if (!_webReady) return;

        if (active)
        {
            HideLoginPanel();
            NavigateToApp();
        }
        else
        {
            ShowLoginPanel(_loginMessage);
        }
    }

    /// <summary>アプリのテーマを WebView2 のカラースキームへ反映する（Web 側の prefers-color-scheme が変わる）</summary>
    private void ApplyThemeToWebView()
    {
        if (!_webReady || Web.CoreWebView2 is null) return;

        Web.CoreWebView2.Profile.PreferredColorScheme = ThemeService.IsDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    private void OnThemeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnThemeChanged));
            return;
        }
        ApplyThemeToWebView();
    }

    private void CalendarTab_Checked(object sender, RoutedEventArgs e) => SelectTab(showSettings: false);

    private void SettingsTab_Checked(object sender, RoutedEventArgs e) => SelectTab(showSettings: true);

    /// <summary>「カレンダー」タブと「設定」タブを切り替える</summary>
    private void SelectTab(bool showSettings)
    {
        // InitializeComponent 中はまだ生成されていないため、その場合は何もしない
        if (SettingsHost is null || _settingsView is null) return;

        if (showSettings)
        {
            _settingsView.Refresh();
            SettingsHost.Visibility = Visibility.Visible;
            Web.Visibility = Visibility.Hidden;
            LoginPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SettingsHost.Visibility = Visibility.Collapsed;
        if (_sessionActive)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            if (_webReady) Web.Visibility = Visibility.Visible;
        }
        else
        {
            ShowLoginPanel(_loginMessage);
        }
    }

    private void OnSettingsSignedOut()
    {
        _sync.Stop();
        CalendarTab.IsChecked = true;
        RequestReLogin();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;

        try
        {
            var userDataFolder = Path.Combine(AppPaths.DataDirectory, "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Web.EnsureCoreWebView2Async(environment);

            var core = Web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnNavigationStarting;

            ApplyThemeToWebView();
            ThemeService.Changed += OnThemeChanged;

            _webReady = true;
        }
        catch (Exception ex)
        {
            AppLog.Write("WebView2 の初期化に失敗しました", ex);
            MessageBox.Show(
                "WebView2 ランタイムが見つかりませんでした。\n" +
                "Microsoft Edge WebView2 ランタイムをインストールしてください。\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "DeLien Calendar",                 MessageBoxButton.OK, MessageBoxImage.Error);
            ShowLoginPanel("WebView2 ランタイムが必要です。", showButton: false);
            return;
        }

        // ログイン状態は App 側で確定しているため、ここでは反映だけ行う
        SetSessionActive(_sessionActive, _loginMessage);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webReady)
        {
            MessageBox.Show("WebView2 ランタイムが見つかりません。", "DeLien Calendar",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoginButton.IsEnabled = false;
        LoginProgress.Visibility = Visibility.Visible;
        LoginMessage.Text = "ブラウザでの許可を待っています…";
        LoginHint.Text = "ブラウザが開かない場合は、既定のブラウザのウィンドウを確認してください。";

        try
        {
            // ブラウザが開かなかった場合に備えて認証URLを画面に出す
            var progress = new Progress<string>(url =>
            {
                LoginHint.Text = "ブラウザが開かない場合は、次のURLをコピーして開いてください。\n" + url;
            });
            var result = await _auth.SignInAsync(progress);
            SecureStore.SaveToken(result.Token);
            _api.Token = result.Token;
            _settings.UserName = result.User?.DiscordName;
            _settings.Save();
            LoginStateChanged?.Invoke(_settings.UserName);

            HideLoginPanel();
            _sessionActive = true;
            if (!string.IsNullOrEmpty(result.SessionUrl))
            {
                // WebView2 側にもログインセッションを確立する
                Web.CoreWebView2.Navigate(result.SessionUrl);
            }
            else
            {
                NavigateToApp();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("端末認証に失敗しました", ex);
            LoginMessage.Text = "ログインできませんでした。もう一度お試しください。";
            LoginHint.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void NavigateToApp()
    {
        if (!_webReady || Web.CoreWebView2 is null) return;
        Web.Visibility = Visibility.Visible;
        Web.CoreWebView2.Navigate(_settings.BaseUrl + "/index.php");
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // カスタムスキーム（discord:// など）は既定アプリに委ねる
        if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        try
        {
            var target = new Uri(e.Uri);
            var appHost = new Uri(_settings.BaseUrl).Host;
            if (string.Equals(target.Host, appHost, StringComparison.OrdinalIgnoreCase))
            {
                // サイト内リンクは同じ WebView 内で開く
                Web.CoreWebView2!.Navigate(e.Uri);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("新しいウィンドウ要求の解析に失敗しました", ex);
        }
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"外部リンクを開けませんでした: {uri}", ex);
        }
    }

    private void ShowLoginPanel(string message, bool showButton = true)
    {
        LoginMessage.Text = message;
        LoginHint.Text = "ブラウザが開きます。Discord でログインし「この端末を許可する」を押してください。";
        LoginButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Hidden;
    }

    private void HideLoginPanel()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose && _settings.CloseToTray)
        {
            // 閉じるボタンはタスクトレイへの格納として扱う
            e.Cancel = true;
            Hide();
            return;
        }

        try
        {
            Web.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write("WebView2 の終了処理に失敗しました", ex);
        }
    }
}
