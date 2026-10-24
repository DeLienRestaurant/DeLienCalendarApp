using System.Text;
using DeLienCalendar.Desktop.Models;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DeLienCalendar.Desktop.Services;

/// <summary>Windows ネイティブのトースト通知 / アラーム表示</summary>
public static class NotificationService
{
    private const string Group = "delien-calendar";

    /// <summary>端末設定に従って予定のリマインドを表示する</summary>
    public static void ShowEventReminder(ApiEvent ev, AppSettings settings)
    {
        var title = string.IsNullOrWhiteSpace(ev.Title) ? "予定" : ev.Title;
        Show(title, BuildBodyLines(ev, settings), ev.Id, settings);
    }

    public static void Show(string title, string body, int eventId, AppSettings settings) =>
        Show(title, new[] { body }, eventId, settings);

    public static void Show(string title, IReadOnlyList<string> bodyLines, int eventId, AppSettings settings)
    {
        try
        {
            var xml = BuildToastXml(title, bodyLines, eventId, settings);
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var toast = new ToastNotification(doc)
            {
                Tag = eventId > 0 ? "ev-" + eventId : "info",
                Group = Group,
            };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            AppLog.Write($"トーストを表示しました[{settings.NotificationStyle}]: {title} / {string.Join(" / ", bodyLines)}");
        }
        catch (Exception ex)
        {
            AppLog.Write("トースト通知の表示に失敗しました", ex);
        }
    }

    /// <summary>通知本文（設定に応じてカレンダー名・説明を付ける）</summary>
    private static List<string> BuildBodyLines(ApiEvent ev, AppSettings settings)
    {
        var time = ev.IsAllDay
            ? $"{ev.StartLocal:M月d日(ddd)} 終日"
            : $"{ev.StartLocal:M月d日(ddd) HH:mm} - {ev.EndLocal:HH:mm}";

        if (settings.ShowCalendarName && !string.IsNullOrWhiteSpace(ev.CalendarName))
        {
            time += $" / {ev.CalendarName}";
        }

        var lines = new List<string> { time };

        if (settings.ShowDescription && !string.IsNullOrWhiteSpace(ev.Description))
        {
            var description = ev.Description.Trim().Replace("\r\n", " ").Replace("\n", " ");
            if (description.Length > 120) description = description[..120] + "…";
            lines.Add(description);
        }

        return lines;
    }

    private static string BuildToastXml(string title, IReadOnlyList<string> bodyLines, int eventId, AppSettings settings)
    {
        var style = settings.NotificationStyle;
        var isAlarm = style == NotificationStyles.Alarm;
        var isSilent = style == NotificationStyles.Silent;

        var xml = new StringBuilder();
        xml.Append(isAlarm
            ? "<toast scenario=\"alarm\" duration=\"long\" activationType=\"foreground\">"
            : "<toast duration=\"long\" activationType=\"foreground\">");

        xml.Append("<visual><binding template=\"ToastGeneric\">");
        xml.Append($"<text>{Escape(title)}</text>");
        foreach (var line in bodyLines)
        {
            xml.Append($"<text>{Escape(line)}</text>");
        }
        xml.Append("<text placement=\"attribution\">DeLien Calendar</text>");
        xml.Append("</binding></visual>");

        if (settings.ShowActions)
        {
            var openArgs = Escape($"open&event={eventId}");
            var snoozeArgs = Escape($"snooze&event={eventId}");
            xml.Append("<actions>");
            xml.Append($"<action content=\"開く\" arguments=\"{openArgs}\" activationType=\"foreground\"/>");
            xml.Append($"<action content=\"スヌーズ(5分)\" arguments=\"{snoozeArgs}\" activationType=\"foreground\"/>");
            xml.Append("</actions>");
        }

        if (isAlarm)
        {
            xml.Append("<audio src=\"ms-winsoundevent:Notification.Looping.Alarm\" loop=\"true\"/>");
        }
        else if (isSilent)
        {
            xml.Append("<audio silent=\"true\"/>");
        }
        else
        {
            xml.Append($"<audio src=\"ms-winsoundevent:{SoundEventName(settings.SoundName)}\"/>");
        }

        xml.Append("</toast>");
        return xml.ToString();
    }

    /// <summary>設定値を ms-winsoundevent 名へ正規化する（不正値は既定音）</summary>
    private static string SoundEventName(string? sound)
    {
        if (string.IsNullOrWhiteSpace(sound)) return Sounds.Default;
        return Sounds.All.Contains(sound) ? sound : Sounds.Default;
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? "";
}

/// <summary>選択可能な通知音（ms-winsoundevent）</summary>
public static class Sounds
{
    public const string Default = "Notification.Default";
    public const string Reminder = "Notification.Reminder";
    public const string Alarm = "Notification.Looping.Alarm";
    public const string Alarm2 = "Notification.Looping.Alarm2";
    public const string Mail = "Notification.Mail";
    public const string IM = "Notification.IM";

    public static readonly string[] All = { Default, Reminder, Alarm, Alarm2, Mail, IM };

    public static string Label(string name) => name switch
    {
        Reminder => "リマインダー",
        Alarm => "アラーム",
        Alarm2 => "アラーム2",
        Mail => "メール",
        IM => "メッセージ",
        _ => "既定",
    };
}
