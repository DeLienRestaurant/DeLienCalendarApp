using System.Text.Json.Serialization;

namespace DeLienCalendar.Desktop.Models;

public sealed class ApiError
{
    [JsonPropertyName("error")] public ApiErrorBody? Error { get; set; }

    public sealed class ApiErrorBody
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

public sealed class DesktopTokenResponse
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("token_id")] public int TokenId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("session_url")] public string? SessionUrl { get; set; }
    [JsonPropertyName("user")] public ApiUser? User { get; set; }
}

public sealed class ApiUser
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("discord_id")] public string? DiscordId { get; set; }
    [JsonPropertyName("discord_name")] public string? DiscordName { get; set; }
    [JsonPropertyName("discord_avatar")] public string? DiscordAvatar { get; set; }
}

public sealed class MeResponse
{
    [JsonPropertyName("server_time")] public string? ServerTime { get; set; }
    [JsonPropertyName("user")] public ApiUser? User { get; set; }
}

public sealed class CalendarListResponse
{
    [JsonPropertyName("calendars")] public List<ApiCalendar> Calendars { get; set; } = new();
}

public sealed class ApiCalendar
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("text_color")] public string? TextColor { get; set; }
    [JsonPropertyName("visibility")] public string? Visibility { get; set; }
    [JsonPropertyName("public_hidden")] public int PublicHidden { get; set; }
    [JsonPropertyName("show_holidays")] public int ShowHolidays { get; set; }
    [JsonPropertyName("permission")] public string? Permission { get; set; }
    [JsonPropertyName("can_edit")] public bool CanEdit { get; set; }
    [JsonPropertyName("reveal_details")] public bool RevealDetails { get; set; }
    [JsonPropertyName("subscribed")] public bool Subscribed { get; set; }
}

public sealed class EventListResponse
{
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("range_truncated")] public bool RangeTruncated { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("events")] public List<ApiEvent> Events { get; set; } = new();
}

public sealed class ApiEvent
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("calendar_id")] public int CalendarId { get; set; }
    [JsonPropertyName("calendar_name")] public string? CalendarName { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("start_at")] public string StartAt { get; set; } = "";
    [JsonPropertyName("end_at")] public string EndAt { get; set; } = "";
    [JsonPropertyName("all_day")] public int AllDay { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("finished")] public int Finished { get; set; }
    [JsonPropertyName("hidden")] public int Hidden { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }

    public DateTime StartLocal => DateTime.TryParse(StartAt, out var d) ? d : DateTime.MinValue;
    public DateTime EndLocal => DateTime.TryParse(EndAt, out var d) ? d : DateTime.MinValue;
    public bool IsAllDay => AllDay == 1;
    public bool IsHidden => Hidden == 1;
}
