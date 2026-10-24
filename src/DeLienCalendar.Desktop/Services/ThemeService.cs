using System.Windows;
using Microsoft.Win32;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// アプリの表示テーマ（ライト / ダーク）を切り替える。
/// テーマは XAML の ResourceDictionary（Themes/*.xaml）で定義し、DynamicResource で参照する。
/// </summary>
public static class ThemeService
{
    public const string SystemMode = "system";
    public const string LightMode = "light";
    public const string DarkMode = "dark";

    public static readonly string[] All = { SystemMode, LightMode, DarkMode };

    /// <summary>設定値（system / light / dark）</summary>
    public static string Mode { get; private set; } = SystemMode;

    /// <summary>実際に適用されているテーマがダークか</summary>
    public static bool IsDark { get; private set; }

    /// <summary>テーマが切り替わったとき</summary>
    public static event Action? Changed;

    public static string Label(string mode) => mode switch
    {
        LightMode => "ライト",
        DarkMode => "ダーク",
        _ => "Windows の設定に合わせる",
    };

    /// <summary>指定のモードを適用する（system の場合は Windows の設定を参照）</summary>
    public static void Apply(string? mode)
    {
        Mode = All.Contains(mode) ? mode! : SystemMode;
        IsDark = Resolve(Mode) == DarkMode;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"/DeLienCalendar;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

        var application = Application.Current;
        if (application is not null)
        {
            if (application.Resources.MergedDictionaries.Count > 0)
            {
                application.Resources.MergedDictionaries[0] = dictionary;
            }
            else
            {
                application.Resources.MergedDictionaries.Add(dictionary);
            }
        }

        AppLog.Write($"表示テーマを適用しました: {Mode}（{(IsDark ? "ダーク" : "ライト")}）");
        Changed?.Invoke();
    }

    private static string Resolve(string mode) => mode switch
    {
        DarkMode => DarkMode,
        LightMode => LightMode,
        _ => IsSystemDark() ? DarkMode : LightMode,
    };

    /// <summary>Windows の「アプリのモード」設定がダークか</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int useLightTheme)
            {
                return useLightTheme == 0;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Windows のテーマ設定を取得できませんでした", ex);
        }

        return false;
    }
}
