using System.IO;
using System.Text.RegularExpressions;

namespace DeLienCalendar.Desktop.Services;

/// <summary>更新履歴の1バージョン分</summary>
public sealed record ChangelogEntry(string Version, string? Date, IReadOnlyList<string> Items);

/// <summary>
/// 同梱の CHANGELOG.md（アプリと同じフォルダ）を読み取る。
/// 書式: 「## v0.2.0 (2026-10-25)」の見出しの下に「- 」の項目を並べる。
/// </summary>
public static class ChangelogService
{
    private const string FileName = "CHANGELOG.md";

    public static IReadOnlyList<ChangelogEntry> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path)) return Array.Empty<ChangelogEntry>();
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception ex)
        {
            AppLog.Write("更新履歴の読み込みに失敗しました", ex);
            return Array.Empty<ChangelogEntry>();
        }
    }

    private static List<ChangelogEntry> Parse(string[] lines)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null;
        string? date = null;
        var items = new List<string>();

        void Flush()
        {
            if (version is not null) entries.Add(new ChangelogEntry(version, date, items.ToArray()));
            items.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                var heading = line[3..].Trim();
                var (parsedVersion, parsedDate) = SplitHeading(heading);
                version = parsedVersion;
                date = parsedDate;
                continue;
            }

            if (version is null || line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                items.Add(line[2..].Trim());
            }
            else if (line.StartsWith("<!--", StringComparison.Ordinal) || line.StartsWith("-->", StringComparison.Ordinal))
            {
                // HTML コメントは無視する
            }
            else
            {
                items.Add(line);
            }
        }

        Flush();
        return entries;
    }

    private static readonly Regex DatePattern = new(@"(\d{4})[-/](\d{1,2})[-/](\d{1,2})", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"[vV]?(\d+(?:\.\d+){1,3})", RegexOptions.Compiled);

    private static (string? Version, string? Date) SplitHeading(string heading)
    {
        // 例: "v0.2.0 (2026-10-25)" / "0.2.0 - 2026-10-25" / "[0.2.0] 2026/10/25"
        string? version = null;
        string? date = null;

        var dateMatch = DatePattern.Match(heading);
        if (dateMatch.Success && DateTime.TryParse(dateMatch.Value.Replace('/', '-'), out var parsed))
        {
            date = parsed.ToString("yyyy-MM-dd");
        }

        // 日付部分を除いてからバージョンを探す（"2026" を誤検出しないように）
        var versionMatch = VersionPattern.Match(DatePattern.Replace(heading, " "));
        if (versionMatch.Success)
        {
            version = versionMatch.Groups[1].Value;
        }

        return (version, date);
    }
}
