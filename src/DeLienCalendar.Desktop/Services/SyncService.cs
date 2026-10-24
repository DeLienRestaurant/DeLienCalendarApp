using DeLienCalendar.Desktop.Models;

namespace DeLienCalendar.Desktop.Services;

/// <summary>予定の定期同期とアラーム登録</summary>
public sealed class SyncService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private System.Threading.Timer? _timer;
    private bool _syncing;

    public SyncService(AppSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;
        State = EventCache.Load();
    }

    public CachedState State { get; private set; }

    /// <summary>同期が完了したとき（UI 更新用）</summary>
    public event Action<CachedState>? Updated;

    /// <summary>同期に失敗したとき（メッセージ）</summary>
    public event Action<string>? Failed;

    public void Start()
    {
        _ = SyncAsync();
        _timer ??= new System.Threading.Timer(_ => _ = SyncAsync(), null, Interval, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public async Task SyncAsync()
    {
        if (_syncing || string.IsNullOrEmpty(_api.Token)) return;

        // 常駐アプリとヘッドレス同期（--sync）の同時実行を防ぐ
        using var gate = SyncGate.TryAcquire();
        if (gate is null) return;

        _syncing = true;
        try
        {
            var calendars = await _api.GetCalendarsAsync().ConfigureAwait(false);
            var eventsResponse = await _api.GetEventsAsync(DateTime.Now.AddHours(-1), DateTime.Now.AddDays(14)).ConfigureAwait(false);

            var scheduled = ReminderScheduler.Apply(eventsResponse.Events, _settings, State.ScheduleHash);

            State = new CachedState
            {
                SyncedAt = DateTime.Now,
                Calendars = calendars,
                Events = eventsResponse.Events,
                ScheduleHash = scheduled.Hash,
            };
            EventCache.Save(State);
            _settings.LastSyncedAt = State.SyncedAt;
            _settings.Save();

            AppLog.Write($"同期完了: カレンダー {calendars.Count} 件 / 予定 {eventsResponse.Events.Count} 件 / アラーム {scheduled.Count} 件");
            Updated?.Invoke(State);
        }
        catch (Exception ex)
        {
            AppLog.Write("同期に失敗しました", ex);
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>次に鳴るアラームの予定を返す（表示用）</summary>
    public ApiEvent? GetNextEvent()
    {
        var now = DateTime.Now;
        return State.Events
            .Where(e => e.Finished == 0 && e.StartLocal >= now)
            .OrderBy(e => e.StartLocal)
            .FirstOrDefault();
    }

    public void Dispose() => Stop();
}
