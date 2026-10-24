using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

    public async Task<AuthResult> SignInAsync(CancellationToken ct = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var stateValue = Uri.EscapeDataString(state);
            var challengeValue = Uri.EscapeDataString(challenge);
            var url = $"{_api.BaseUrl}/desktop/login.php?port={port}&challenge={challengeValue}&state={stateValue}";
            OpenBrowser(url);

            var code = await WaitForCodeAsync(listener, state, ct).ConfigureAwait(false);
            var result = await _api.ExchangeDesktopCodeAsync(code, verifier, DeviceName(), ct).ConfigureAwait(false);
            return new AuthResult(result.Token, result.SessionUrl, result.User);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>ループバックへのリクエストを待ち、state を検証して code を取り出す</summary>
    private static async Task<string> WaitForCodeAsync(TcpListener listener, string expectedState, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("端末認証がタイムアウトしました。もう一度お試しください。");
            }

            using (client)
            {
                var requestLine = await ReadRequestLineAsync(client, timeoutCts.Token).ConfigureAwait(false);
                var target = ExtractPath(requestLine);

                if (!target.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
                {
                    // favicon などの余計なリクエストは無視する
                    await WriteResponseAsync(client, 404, "Not Found", "").ConfigureAwait(false);
                    continue;
                }

                var query = ParseQuery(target);
                query.TryGetValue("state", out var state);
                if (!string.IsNullOrEmpty(expectedState) && !string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    await WriteResponseAsync(client, 400, "端末認証に失敗しました", "state が一致しません。アプリからやり直してください。").ConfigureAwait(false);
                    throw new InvalidOperationException("端末認証の state が一致しませんでした。");
                }

                if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                {
                    await WriteResponseAsync(client, 400, "端末認証に失敗しました", "認証コードが受け取れませんでした。").ConfigureAwait(false);
                    throw new InvalidOperationException("端末認証コードを取得できませんでした。");
                }

                await WriteResponseAsync(client, 200, "端末を連携しました",
                    "このタブは閉じて、アプリに戻ってください。").ConfigureAwait(false);
                return code;
            }
        }
    }

    private static async Task<string> ReadRequestLineAsync(TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        // ヘッダを読み捨ててリクエスト行だけ返す
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? "";
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) break;
        }
        return requestLine;
    }

    private static async Task WriteResponseAsync(TcpClient client, int status, string title, string message)
    {
        var html = $$"""
            <!DOCTYPE html><html lang="ja"><head><meta charset="utf-8">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;background:#f6f7fb;color:#1f2333;
            display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
            .card{background:#fff;border-radius:16px;padding:32px 40px;box-shadow:0 10px 30px rgba(31,35,51,.12);text-align:center}
            h1{font-size:20px;margin:0 0 12px}p{margin:0;color:#555b6e}</style></head>
            <body><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1>
            <p>{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");

        var stream = client.GetStream();
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string ExtractPath(string requestLine)
    {
        var parts = requestLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = target.IndexOf('?');
        if (index < 0 || index == target.Length - 1) return result;
        foreach (var pair in target[(index + 1)..].Split('&'))
        {
            if (pair.Length == 0) continue;
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            result[key] = value;
        }
        return result;
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string DeviceName()
    {
        var name = Environment.MachineName;
        return name.Length > 64 ? name[..64] : name;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
