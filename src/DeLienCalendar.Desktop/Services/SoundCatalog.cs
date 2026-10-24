using System.IO;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 通知・アラームで使う音声の一覧と解決。
/// 指定形式: <c>builtin:Alarm01</c>（Windows の Media フォルダ）または <c>custom:C:\path\sound.wav</c>
/// </summary>
public static class SoundCatalog
{
    public const string BuiltinPrefix = "builtin:";
    public const string CustomPrefix = "custom:";

    public const string DefaultSound = "builtin:Alarm01";

    /// <summary>プリセット音（%WINDIR%\Media 配下の wav）</summary>
    private static readonly (string Id, string Label)[] Builtins =
    {
        ("Alarm01", "アラーム 1"),
        ("Alarm02", "アラーム 2"),
        ("Alarm03", "アラーム 3"),
        ("Alarm04", "アラーム 4"),
        ("Alarm05", "アラーム 5"),
        ("Ring01", "呼び出し 1"),
        ("Ring02", "呼び出し 2"),
        ("Ring03", "呼び出し 3"),
        ("chimes", "チャイム"),
        ("ding", "ディン"),
        ("notify", "通知音"),
        ("tada", "ファンファーレ"),
        ("Windows Ding", "Windows ディン"),
        ("Windows Balloon", "Windows バルーン"),
        ("Windows Default", "Windows 既定"),
    };

    public static IReadOnlyList<(string Id, string Label)> Items => Builtins;

    private static string MediaDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>設定値から再生するファイルの絶対パスを求める（見つからなければ null）</summary>
    public static string? ResolvePath(string? soundFile)
    {
        if (string.IsNullOrWhiteSpace(soundFile)) return null;

        if (soundFile.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = soundFile[BuiltinPrefix.Length..];
            var path = Path.Combine(MediaDirectory, name + ".wav");
            return File.Exists(path) ? path : null;
        }

        if (soundFile.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = soundFile[CustomPrefix.Length..];
            return File.Exists(path) ? path : null;
        }

        // 旧設定や素のパスも受け付ける
        return File.Exists(soundFile) ? soundFile : null;
    }

    /// <summary>設定値の表示名</summary>
    public static string Label(string? soundFile)
    {
        if (string.IsNullOrWhiteSpace(soundFile)) return "（なし）";

        if (soundFile.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = soundFile[BuiltinPrefix.Length..];
            var match = Builtins.FirstOrDefault(b => string.Equals(b.Id, name, StringComparison.OrdinalIgnoreCase));
            return match.Label ?? name;
        }

        if (soundFile.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(soundFile[CustomPrefix.Length..]);
        }

        return Path.GetFileName(soundFile);
    }

    /// <summary>設定値を正規化する（不正なら既定値）</summary>
    public static string Normalize(string? soundFile)
    {
        if (string.IsNullOrWhiteSpace(soundFile)) return DefaultSound;
        return ResolvePath(soundFile) is null ? DefaultSound : soundFile;
    }
}
