using System.Threading;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 単一インスタンス制御。
/// 2つ目の起動は既存インスタンスへ「表示」を通知して終了する。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\DeLienCalendar.Desktop";
    private const string ShowEventName = @"Local\DeLienCalendar.Desktop.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <summary>単一インスタンスを確保できた場合は true。</summary>
    public static bool TryAcquire(out SingleInstance? instance)
    {
        instance = null;
        var mutex = new Mutex(true, MutexName, out var created);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!created)
        {
            mutex.Dispose();
            showEvent.Dispose();
            return false;
        }
        instance = new SingleInstance(mutex, showEvent);
        return true;
    }

    /// <summary>既存インスタンスが動作中かどうか</summary>
    public static bool IsRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write("既存インスタンスの確認に失敗しました", ex);
            return false;
        }
    }

    /// <summary>既存インスタンスに表示を要求する。</summary>
    public static void SignalExisting()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ShowEventName);
            handle.Set();
        }
        catch (Exception ex)
        {
            AppLog.Write("既存インスタンスへの通知に失敗しました", ex);
        }
    }

    /// <summary>他の起動要求を監視し、要求されたら onShow を呼ぶ。</summary>
    public void StartListening(Action onShow)
    {
        var token = _cts.Token;
        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(new[] { _showEvent, token.WaitHandle }, TimeSpan.FromSeconds(5));
                if (token.IsCancellationRequested) return;
                if (signaled == 0) onShow();
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _showEvent.Dispose();
        _mutex.Dispose();
        _cts.Dispose();
    }
}
