using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 端末認証用のループバック HTTP 受け口。
///
/// ブラウザから <c>/callback?code=...&amp;state=...</c> へリダイレクトされてくる
/// 1 件のリクエストを受け取り、完了ページを返して終了する。
/// 接続ごとにストリームを使い回すため、読み取りでソケットを閉じてしまわないよう注意すること。
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private readonly TcpListener _listener;

    private LoopbackListener(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>待ち受けポート</summary>
    public int Port { get; }

    /// <summary>空きポートで待ち受けを開始する</summary>
    public static LoopbackListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new LoopbackListener(listener, port);
    }

    /// <summary>認証コードを受け取るまで待機する</summary>
    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("端末認証がタイムアウトしました。もう一度お試しください。");
            }
            catch (ObjectDisposedException)
            {
                throw new TimeoutException("端末認証が中断されました。");
            }

            using (client)
            {
                var code = await HandleConnectionAsync(client, expectedState, token).ConfigureAwait(false);
                if (code is not null) return code;
            }
        }
    }

    /// <summary>1件の接続を処理する。認証コードが取れた場合のみ返す。</summary>
    private static async Task<string?> HandleConnectionAsync(TcpClient client, string expectedState, CancellationToken ct)
    {
        NetworkStream stream;
        string requestLine;
        try
        {
            stream = client.GetStream();
            requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            // 接続が途中で切れた場合は次の接続を待つ
            return null;
        }

        var target = ExtractPath(requestLine);
        var query = ParseQuery(target);

        if (!target.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
        {
            // favicon などの余計なリクエストは無視して待ち続ける
            await TryWriteResponseAsync(stream, 404, "Not Found", "").ConfigureAwait(false);
            return null;
        }

        query.TryGetValue("state", out var state);
        if (!string.IsNullOrEmpty(expectedState) && !string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await TryWriteResponseAsync(stream, 400, "端末認証に失敗しました",
                "state が一致しません。アプリからやり直してください。").ConfigureAwait(false);
            return null;
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            await TryWriteResponseAsync(stream, 400, "端末認証に失敗しました",
                "認証コードが受け取れませんでした。").ConfigureAwait(false);
            return null;
        }

        await TryWriteResponseAsync(stream, 200, "端末を連携しました",
            "このタブは閉じて、アプリに戻ってください。").ConfigureAwait(false);
        return code;
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        // leaveOpen: true にしないとソケットごと閉じてしまい、応答を書き込めなくなる
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? "";

        // ヘッダは読み捨てる
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) break;
        }
        return requestLine;
    }

    /// <summary>応答を書き込む。ブラウザが既に切断していても例外は握りつぶす。</summary>
    private static async Task TryWriteResponseAsync(NetworkStream stream, int status, string title, string message)
    {
        try
        {
            var html = BuildHtml(title, message);
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(header).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            AppLog.Write("端末認証ページの応答に失敗しました（ブラウザ側で切断された可能性）", ex);
        }
    }

    private static string BuildHtml(string title, string message) => $$"""
        <!DOCTYPE html><html lang="ja"><head><meta charset="utf-8">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;background:#f6f7fb;color:#1f2333;
        display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
        .card{background:#fff;border-radius:16px;padding:32px 40px;box-shadow:0 10px 30px rgba(31,35,51,.12);text-align:center}
        h1{font-size:20px;margin:0 0 12px}p{margin:0;color:#555b6e}</style></head>
        <body><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1>
        <p>{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
        """;

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

    public void Dispose() => _listener.Stop();
}
