namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 同期処理の排他制御。
/// 常駐アプリとヘッドレス同期（--sync）が同時に cache.json を書き換えないようにする。
/// </summary>
/// <summary>
/// 同期処理の排他制御。
/// 常駐アプリとヘッドレス同期（--sync）が同時に cache.json を書き換えないようにする。
///
/// Mutex は取得したスレッドでしか解放できないため、await をまたぐ
/// 非同期処理では使えない。ここでは名前付き Semaphore（スレッド非依存）を使う。
/// </summary>
public sealed class SyncGate : IDisposable
{
    private const string Name = @"Local\DeLienCalendar.Sync";

    private readonly Semaphore _semaphore;

    private SyncGate(Semaphore semaphore) => _semaphore = semaphore;

    /// <summary>取得できた場合は同期してよい。他プロセスが同期中なら null。</summary>
    public static SyncGate? TryAcquire()
    {
        try
        {
            var semaphore = new Semaphore(1, 1, Name);
            if (semaphore.WaitOne(TimeSpan.Zero)) return new SyncGate(semaphore);
            semaphore.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write("同期ロックの取得に失敗しました", ex);
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _semaphore.Release();
        }
        catch (Exception ex)
        {
            AppLog.Write("同期ロックの解放に失敗しました", ex);
        }
        _semaphore.Dispose();
    }
}
