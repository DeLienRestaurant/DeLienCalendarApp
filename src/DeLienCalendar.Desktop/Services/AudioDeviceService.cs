using NAudio.CoreAudioApi;

namespace DeLienCalendar.Desktop.Services;

/// <summary>再生に使う出力デバイス</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>Windows の音声出力デバイス一覧</summary>
public static class AudioDeviceService
{
    /// <summary>Windows の既定デバイスを表す ID</summary>
    public const string SystemDefault = "";

    public static List<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice> { new(SystemDefault, "Windows の既定デバイス") };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDevice(device.ID, device.FriendlyName));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("出力デバイスの取得に失敗しました", ex);
        }
        return devices;
    }

    /// <summary>指定 ID のデバイスを取得する（既定なら null）</summary>
    public static MMDevice? Find(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId);
        }
        catch (Exception ex)
        {
            // デバイスが抜かれた場合は既定デバイスで再生する
            AppLog.Write($"出力デバイスが見つかりません（既定で再生します）: {deviceId}", ex);
            return null;
        }
    }
}
