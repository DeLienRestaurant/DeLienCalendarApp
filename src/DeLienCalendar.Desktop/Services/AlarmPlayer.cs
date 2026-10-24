using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 通知音・アラーム音の再生。
/// Windows のトースト通知は任意の音声ファイルや出力デバイスを指定できないため、
/// 音はアプリ側で再生し、トーストは無音で表示する。
/// </summary>
public sealed class AlarmPlayer : IDisposable
{
    private readonly object _gate = new();

    private WasapiOut? _output;
    private AudioFileReader? _reader;
    private bool _loop;
    private bool _stopping;
    private bool _disposed;

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _output is not null && !_stopping;
        }
    }

    /// <summary>
    /// 再生する。<paramref name="loop"/> が true の場合は停止するまで繰り返す。
    /// <paramref name="maxDuration"/> を指定すると、その時間で打ち切る。
    /// </summary>
    public async Task PlayAsync(string path, string? deviceId, bool loop, TimeSpan? maxDuration = null)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            StopCore();

            var device = AudioDeviceService.Find(deviceId);
            var output = device is null
                ? new WasapiOut(AudioClientShareMode.Shared, 200)
                : new WasapiOut(device, AudioClientShareMode.Shared, false, 200);

            var reader = new AudioFileReader(path);
            output.Init(reader);

            _output = output;
            _reader = reader;
            _loop = loop;
            _stopping = false;

            output.PlaybackStopped += (_, _) =>
            {
                bool restart;
                lock (_gate) restart = _loop && !_stopping && !_disposed;

                if (restart)
                {
                    try
                    {
                        _reader!.Position = 0;
                        _output!.Play();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("アラーム音の再再生に失敗しました", ex);
                        completion.TrySetResult(true);
                    }
                }
                else
                {
                    completion.TrySetResult(true);
                }
            };

            output.Play();
        }

        if (maxDuration is null)
        {
            await completion.Task.ConfigureAwait(false);
            return;
        }

        await Task.WhenAny(completion.Task, Task.Delay(maxDuration.Value)).ConfigureAwait(false);
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        _stopping = true;
        try
        {
            _output?.Stop();
        }
        catch (Exception ex)
        {
            AppLog.Write("アラーム音の停止に失敗しました", ex);
        }

        try
        {
            _output?.Dispose();
        }
        catch
        {
            // 破棄時の失敗は無視する
        }

        _output = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopCore();
        }
    }
}
