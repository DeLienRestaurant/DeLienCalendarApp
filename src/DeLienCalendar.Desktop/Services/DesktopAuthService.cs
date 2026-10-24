using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DeLienCalendar.Desktop.Models;

namespace DeLienCalendar.Desktop.Services;

/// <summary>ログイン結果</summary>
public sealed record AuthResult(string Token, string? SessionUrl, ApiUser? User);

/// <summary>
/// デスクトップ端末認証（ブラウザ経由ループバック + PKCE）
///
/// 1. 127.0.0.1:{任意ポート} で一時的な HTTP サーバを起動する
/// 2. 既定ブラウザで /desktop/login.php を開き、ユーザーが許可する
/// 3. サーバがワンタイムコードをループバックへ返す
/// 4. コードを PKCE 付きで /api/v1/auth/desktop/token に送り端末トークンに交換する
/// </summary>
public sealed class DesktopAuthService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private readonly ApiClient _api;

    public DesktopAuthService(ApiClient api) => _api = api;

    public async Task<AuthResult> SignInAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        using var listener = LoopbackListener.Start();

        var url = $"{_api.BaseUrl}/desktop/login.php" +
                  $"?port={listener.Port}" +
                  $"&challenge={Uri.EscapeDataString(challenge)}" +
                  $"&state={Uri.EscapeDataString(state)}";

        AppLog.Write($"端末認証を開始しました（待ち受けポート {listener.Port}）");
        progress?.Report(url);
        OpenBrowser(url);

        var code = await listener.WaitForCodeAsync(state, Timeout, ct).ConfigureAwait(false);
        var result = await _api.ExchangeDesktopCodeAsync(code, verifier, DeviceName(), ct).ConfigureAwait(false);
        return new AuthResult(result.Token, result.SessionUrl, result.User);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("ブラウザを起動できませんでした: " + url, ex);
            throw new InvalidOperationException(
                "ブラウザを起動できませんでした。既定のブラウザが設定されているか確認してください。", ex);
        }
    }

    private static string DeviceName()
    {
        var name = Environment.MachineName;
        return name.Length > 64 ? name[..64] : name;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
