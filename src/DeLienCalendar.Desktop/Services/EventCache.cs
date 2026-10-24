using System.IO;
using System.Text.Json;
using DeLienCalendar.Desktop.Models;

namespace DeLienCalendar.Desktop.Services;

/// <summary>直近に同期したカレンダー・予定のローカルキャッシュ</summary>
public sealed class CachedState
{
    public DateTime SyncedAt { get; set; }
    public List<ApiCalendar> Calendars { get; set; } = new();
    public List<ApiEvent> Events { get; set; } = new();

    /// <summary>登録済みアラームの内容ハッシュ（再登録の要否判定に使う）</summary>
    public string? ScheduleHash { get; set; }
}

public static class EventCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "cache.json");

    public static CachedState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<CachedState>(File.ReadAllText(FilePath), JsonOptions);
                if (state is not null)
                {
                    state.Calendars ??= new List<ApiCalendar>();
                    state.Events ??= new List<ApiEvent>();
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("キャッシュの読み込みに失敗しました", ex);
        }
        return new CachedState();
    }

    public static void Save(CachedState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Write("キャッシュの保存に失敗しました", ex);
        }
    }
}
