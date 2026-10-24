using System.Diagnostics;
using System.IO;
using System.Text;
using DeLienCalendar.Desktop.Models;

namespace DeLienCalendar.Desktop.Services;

/// <summary>
/// 未起動時でも鳴るアラームの登録。
///
/// MSIX パッケージ（＝コード署名）を使わない方針のため、Windows の
/// タスクスケジューラに「1回だけ実行」のタスクを登録し、時刻になったら
/// アプリを <c>--notify {eventId}</c> 付きで起動させてトーストを表示する。
/// タスクは XML 定義（ロケール非依存）で作成する。
/// </summary>
public static class ReminderScheduler
{
    private const string TaskPrefix = "DeLienCalendar-";
    private const string SyncTaskName = "DeLienCalendar-Sync";

    /// <summary>アラーム登録の結果</summary>
    public sealed record ScheduleResult(int Count, string Hash);

    /// <summary>登録すべきリマインド一覧を計算してタスクスケジューラへ反映する</summary>
    public static ScheduleResult Apply(IReadOnlyList<ApiEvent> events, AppSettings settings, string? previousHash)
    {
        if (!settings.NotificationsEnabled)
        {
            RemoveOwnedTasks(includeSyncTask: false);
            return new ScheduleResult(0, "");
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return new ScheduleResult(0, previousHash ?? "");

        var plan = BuildPlan(events, settings);
        var hash = string.Join("|", plan.Select(p => $"{p.Name}@{p.When:yyyyMMddHHmm}"));
        if (hash == previousHash) return new ScheduleResult(plan.Count, hash);

        RemoveOwnedTasks(includeSyncTask: false);
        var tasksDir = Path.Combine(AppPaths.DataDirectory, "tasks");
        Directory.CreateDirectory(tasksDir);

        var created = 0;
        foreach (var item in plan)
        {
            var xmlPath = Path.Combine(tasksDir, item.Name + ".xml");
            try
            {
                File.WriteAllText(xmlPath, BuildTaskXml(item, exe), Encoding.Unicode);
                if (RunSchtasks($"/Create /TN \"{item.Name}\" /XML \"{xmlPath}\" /F", out _))
                {
                    created++;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"アラームの登録に失敗しました: {item.Name}", ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(xmlPath)) File.Delete(xmlPath);
                }
                catch
                {
                    // 一時ファイルの削除失敗は無視する
                }
            }
        }
        AppLog.Write($"アラームを {created} 件登録しました。");
        return new ScheduleResult(created, hash);
    }

    private sealed record PlannedReminder(string Name, DateTime When, int EventId);

    private static List<PlannedReminder> BuildPlan(IReadOnlyList<ApiEvent> events, AppSettings settings)
    {
        var plan = new List<PlannedReminder>();
        var now = DateTime.Now;
        var horizon = now.AddDays(14);

        foreach (var ev in events)
        {
            if (settings.NotifyCalendarIds.Count > 0 && !settings.NotifyCalendarIds.Contains(ev.CalendarId)) continue;
            if (ev.Finished == 1) continue;

            var start = ev.StartLocal;
            if (start == DateTime.MinValue) continue;

            // 終日予定は当日の 9:00 に通知する（時刻の概念がないため）
            var baseTime = ev.IsAllDay ? start.Date.AddHours(9) : start;
            var notifyAt = baseTime.AddMinutes(-settings.ReminderMinutes);

            if (notifyAt <= now || notifyAt > horizon) continue;

            plan.Add(new PlannedReminder(
                $"{TaskPrefix}{ev.Id}-{notifyAt:yyyyMMddHHmm}",
                notifyAt,
                ev.Id));
        }

        return plan.OrderBy(p => p.When).ToList();
    }

    private static string BuildTaskXml(PlannedReminder item, string exe)
    {
        var boundary = item.When.ToString("yyyy-MM-ddTHH:mm:ss");
        var escapedExe = System.Security.SecurityElement.Escape(exe) ?? exe;
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>DeLien Calendar の予定リマインド</Description>
                <URI>\{item.Name}</URI>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <IdleSettings>
                  <Duration>PT10M</Duration>
                  <WaitTimeout>PT1H</WaitTimeout>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <WakeToRun>true</WakeToRun>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <Priority>7</Priority>
              </Settings>
              <Triggers>
                <TimeTrigger>
                  <StartBoundary>{boundary}</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{escapedExe}</Command>
                  <Arguments>--notify {item.EventId}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>このアプリが登録したタスクを削除する（同期タスクは既定で残す）</summary>
    public static void RemoveOwnedTasks(bool includeSyncTask = true)
    {
        try
        {
            if (!RunSchtasks("/Query /FO CSV /NH", out var output)) return;
            foreach (var line in output.Split('\n'))
            {
                // CSV の1列目は "\DeLienCalendar-..." のようにルートフォルダ表記で返る
                var name = line.Split(',')[0].Trim().Trim('"').TrimStart('\\');
                if (!name.StartsWith(TaskPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!includeSyncTask && string.Equals(name, SyncTaskName, StringComparison.OrdinalIgnoreCase)) continue;
                RunSchtasks($"/Delete /TN \"{name}\" /F", out _);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("登録済みアラームの削除に失敗しました", ex);
        }
    }

    /// <summary>
    /// 15分ごとにヘッドレス同期（--sync）を実行するタスクを登録する。
    /// アプリを起動していない間も予定の変更へ追従するために使う。
    /// </summary>
    public static bool EnsureSyncTask(int intervalMinutes = 15)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var xml = BuildSyncTaskXml(exe, intervalMinutes);
        var xmlPath = Path.Combine(AppPaths.DataDirectory, "tasks", SyncTaskName + ".xml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            var ok = RunSchtasks($"/Create /TN \"{SyncTaskName}\" /XML \"{xmlPath}\" /F", out var output);
            if (ok) AppLog.Write($"同期タスクを登録しました（{intervalMinutes}分間隔）");
            else AppLog.Write($"同期タスクの登録に失敗しました: {output}");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Write("同期タスクの登録に失敗しました", ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
            }
            catch
            {
                // 一時ファイルの削除失敗は無視する
            }
        }
    }

    /// <summary>同期タスクを削除する（連携解除時など）</summary>
    public static void RemoveSyncTask() => RunSchtasks($"/Delete /TN \"{SyncTaskName}\" /F", out _);

    private static string BuildSyncTaskXml(string exe, int intervalMinutes)
    {
        if (intervalMinutes < 5) intervalMinutes = 5;
        var escapedExe = System.Security.SecurityElement.Escape(exe) ?? exe;
        var start = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:00");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>DeLien Calendar の予定を定期的に同期します</Description>
                <URI>\{SyncTaskName}</URI>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <IdleSettings>
                  <Duration>PT10M</Duration>
                  <WaitTimeout>PT1H</WaitTimeout>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <Priority>7</Priority>
              </Settings>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                  <Repetition>
                    <Interval>PT{intervalMinutes}M</Interval>
                    <StopAtDurationEnd>false</StopAtDurationEnd>
                  </Repetition>
                  <ScheduleByDay>
                    <DaysInterval>1</DaysInterval>
                  </ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{escapedExe}</Command>
                  <Arguments>--sync</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static bool RunSchtasks(string arguments, out string output)
    {
        output = "";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (process is null) return false;
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLog.Write("schtasks の実行に失敗しました: " + arguments, ex);
            return false;
        }
    }
}
