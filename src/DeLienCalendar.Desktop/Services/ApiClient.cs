using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeLienCalendar.Desktop.Models;

namespace DeLienCalendar.Desktop.Services;

/// <summary>DeLien Calendar Web API（/api/v1）クライアント</summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _baseUrl;

    public ApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string? Token { get; set; }

    /// <summary>接続先ベースURL</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>端末認証コードをトークンに交換する（認証不要）</summary>
    public async Task<DesktopTokenResponse> ExchangeDesktopCodeAsync(string code, string verifier, string deviceName, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { code, verifier, device_name = deviceName }, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await Http.PostAsync($"{_baseUrl}/api/v1/auth/desktop/token", content, ct).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new ApiException(res.StatusCode, json);
        return JsonSerializer.Deserialize<DesktopTokenResponse>(json, JsonOptions)
               ?? throw new ApiException(res.StatusCode, "応答を解析できませんでした。");
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/api/v1/me", ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MeResponse>(json, JsonOptions) ?? new MeResponse();
    }

    public async Task<List<ApiCalendar>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/api/v1/me/calendars", ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CalendarListResponse>(json, JsonOptions)?.Calendars ?? new List<ApiCalendar>();
    }

    public async Task<EventListResponse> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var query = $"?from={Uri.EscapeDataString(from.ToString("yyyy-MM-dd HH:mm:ss"))}" +
                    $"&to={Uri.EscapeDataString(to.ToString("yyyy-MM-dd HH:mm:ss"))}";
        var json = await GetAsync("/api/v1/me/events" + query, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EventListResponse>(json, JsonOptions) ?? new EventListResponse();
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/auth/desktop/revoke");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        // 失効済みでも成功扱いにする
    }

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        if (!string.IsNullOrEmpty(Token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new ApiException(res.StatusCode, json);
        return json;
    }
}

public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string body) : base(Format(status, body))
    {
        Status = status;
        Body = body;
    }

    public HttpStatusCode Status { get; }
    public string Body { get; }

    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;

    private static string Format(HttpStatusCode status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                return $"{(int)status} {msg.GetString()}";
            }
        }
        catch
        {
            // JSON でない応答はそのまま扱う
        }
        return $"{(int)status} 通信に失敗しました。";
    }
}
