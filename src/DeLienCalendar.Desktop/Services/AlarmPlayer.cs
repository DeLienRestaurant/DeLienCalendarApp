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
    private bool _disposed;

    /// <summary>
    /// 再生する。<paramref name="loop"/> が true の場合は停止するまで繰り返す。
    /// <paramref name="maxDuration"/> を指定すると、その時間で打ち切る。
    /// 返り値のタスクは、再生が終わるか <see cref="Stop"/> されるまで完了しない。
    /// </summary>
    public async Task PlayAsync(string path, string? deviceId, bool loop, TimeSpan? maxDuration = null)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        WasapiOut? previousOutput;
        AudioFileReader? previousReader;
        lock (_gate)
        {
            if (_disposed) return;
            previousOutput = _output;
            previousReader = _reader;
            _output = null;
            _reader = null;
        }
        // 停止はロックの外で行う（PlaybackStopped からの再入でデッドロックしないように）
        ReleaseOutput(previousOutput, previousReader);

        WasapiOut output;
        AudioFileReader reader;
        try
        {
            var device = AudioDeviceService.Find(deviceId);
            output = device is null
                ? new WasapiOut(AudioClientShareMode.Shared, 200)
                : new WasapiOut(device, AudioClientShareMode.Shared, false, 200);
            reader = new AudioFileReader(path);
            output.Init(reader);
        }
        catch (Exception ex)
        {
            AppLog.Write($"アラーム音を読み込めませんでした: {path}", ex);
            return;
        }

        output.PlaybackStopped += (_, e) =>
        {
            bool restart;
            lock (_gate) restart = loop && !_disposed && ReferenceEquals(_output, output);

            if (restart)
            {
                try
                {
                    reader.Position = 0;
                    output.Play();
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Write("アラーム音の再再生に失敗しました", ex);
                }
            }

            if (e.Exception is not null) AppLog.Write("アラーム音の再生に失敗しました", e.Exception);
            completion.TrySetResult(true);
        };

        bool disposedMeanwhile;
        lock (_gate)
        {
            disposedMeanwhile = _disposed;
            if (!disposedMeanwhile)
            {
                _output = output;
                _reader = reader;
            }
        }
        if (disposedMeanwhile)
        {
            ReleaseOutput(output, reader);
            return;
        }

        try
        {
            output.Play();
        }
        catch (Exception ex)
        {
            AppLog.Write("アラーム音の再生に失敗しました", ex);
            Stop();
            return;
        }

        if (maxDuration is null)
        {
            await completion.Task.ConfigureAwait(false);
            return;
        }

        var finished = await Task.WhenAny(completion.Task, Task.Delay(maxDuration.Value)).ConfigureAwait(false);
        if (finished != completion.Task) Stop();
    }

    public void Stop()
    {
        WasapiOut? output;
        AudioFileReader? reader;
        lock (_gate)
        {
            output = _output;
            reader = _reader;
            _output = null;
            _reader = null;
        }
        ReleaseOutput(output, reader);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }

    private static void ReleaseOutput(WasapiOut? output, AudioFileReader? reader)
    {
        if (output is not null)
        {
            try
            {
                output.Stop();
            }
            catch (Exception ex)
            {
                AppLog.Write("アラーム音の停止に失敗しました", ex);
            }

            try
            {
                output.Dispose();
            }
            catch
            {
                // 破棄時の失敗は無視する
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
            // 破棄時の失敗は無視する
        }
    }
}
