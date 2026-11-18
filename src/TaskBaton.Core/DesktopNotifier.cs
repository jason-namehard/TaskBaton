using System.ComponentModel;
using System.Text.Json;

namespace TaskBaton.Core;

public sealed class DesktopNotifier(string? executable, bool muteAudio = false, bool suppressDisplay = false) : IDesktopNotifier
{
    public Task<ResumeResult> TestAsync(string home,string title,string message,CancellationToken cancellationToken=default) =>
        InvokeAsync(home,["--test","--title",title,"--message",message],cancellationToken);

    public async Task<ResumeResult> NotifyAsync(string home, TaskRecord task, CancellationToken cancellationToken)
        => await InvokeAsync(Path.GetDirectoryName(task.RequestPath)!,["--notify","--home",home,"--task",task.TaskId,"--attempt",task.AttemptId],cancellationToken);

    private async Task<ResumeResult> InvokeAsync(string directory,IEnumerable<string> request,CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new ResumeResult("unavailable", "desktop_platform", "桌面通知当前仅支持 Windows。");
        if (executable is null) return new ResumeResult("unavailable", "desktop_unconfigured", "请使用 desktop configure 登记通知程序；结果与提示词已保存在 Inbox。");
        if (!File.Exists(executable)) return new ResumeResult("unavailable", "desktop_executable_missing", "通知辅助程序已不存在，请使用当前发布包重新启用通知。任务和回执仍已保存。");
        var report = Path.Combine(directory, "desktop-" + Guid.NewGuid() + ".json");
        try
        {
            var arguments = request.Concat(["--report-path",report]).ToList();
            if (muteAudio) arguments.Add("--silent");
            if (suppressDisplay) arguments.Add("--history-only");
            var process = await RelayProcess.RunAsync(executable,arguments,null,TimeSpan.FromSeconds(30),cancellationToken);
            if (process.TimedOut) return new ResumeResult("unknown", "desktop_timed_out", "通知调用超时，可能已提交，不会自动重复发送。");
            if (!File.Exists(report)) return new ResumeResult("failed", "desktop_report_missing", "通知程序没有返回报告：" + process.Error);
            var result = RelayJson.Read<ResumeResult>(File.ReadAllText(report));
            if (result.State is not ("submitted" or "failed" or "unavailable"))
                return new ResumeResult("failed", "invalid_desktop_report", "通知程序返回了无效状态；任务和回执仍已保存。");
            if (process.ExitCode != 0 && result.State == "submitted")
                return new ResumeResult("unknown", "desktop_exit_failed", "通知可能已提交，辅助程序异常退出。");
            return result;
        }
        // Notification programs and their reports are an external process boundary, independent from task completion.
        catch (Win32Exception error) { return new("failed","desktop_start_failed",error.Message); }
        catch (IOException error) { return new("failed","desktop_file_error",error.Message); }
        catch (UnauthorizedAccessException error) { return new("failed","desktop_access_denied",error.Message); }
        catch (JsonException error) { return new("failed","desktop_report_invalid",error.Message); }
        finally { if (File.Exists(report)) File.Delete(report); }
    }
}

public sealed partial class RelayService
{
    public string? DesktopExecutable() => store.Read(s => s.Query("SELECT executable FROM desktop WHERE id=1", r => r.GetString(0)).SingleOrDefault());

    public void EnableDesktopFallback(string expectedPackageExecutable)
    {
        var full=Path.GetFullPath(expectedPackageExecutable);
        // The user enabled notifications, but native support or the packaged helper is unavailable.
        // Keep that choice so the GUI can show its own notification without claiming native verification.
        store.Write(s=>s.Execute("INSERT INTO desktop VALUES(1,$exe) ON CONFLICT(id) DO UPDATE SET executable=excluded.executable",("$exe",full)));
    }

    public string? RebindDesktopExecutable(string currentPackageExecutable)
    {
        if (DesktopExecutable() is not { } registered) return null;
        var full=Path.GetFullPath(currentPackageExecutable);
        if (!File.Exists(full)) throw new RelayException("desktop_executable_missing","当前发布包缺少通知辅助程序，请完整解压 Windows 包；任务和回执仍可正常使用。");
        if (string.Equals(registered,full,StringComparison.OrdinalIgnoreCase)) return registered;
        store.Write(s=>s.Execute("UPDATE desktop SET executable=$exe WHERE id=1",("$exe",full)));
        return full;
    }

    public DesktopNotificationContent NotificationContent(TaskRecord task)
    {
        var agents=Agents();
        string Name(string id)=>agents.SingleOrDefault(a=>a.AgentId==id)?.DisplayName??id;
        var waiting=Name(task.AwaitingAgentId??task.RecipientId);
        var title=task.State switch
        {
            TaskState.Succeeded=>task.Collaboration?.Deliverable is not null?"协作已交付":"交接已完成",
            TaskState.Paused=>"交接已暂停，等待你决定",
            TaskState.Failed or TaskState.TimedOut=>"交接需要处理",
            TaskState.Cancelled=>"交接已取消",
            TaskState.Acknowledged or TaskState.Running or TaskState.Dispatched=>waiting+" 正在处理",
            TaskState.AwaitingReply=>"等待 "+waiting+" 继续处理",
            _=>"等待 "+waiting+" 接收并回复"
        };
        var instruction=task.State switch
        {
            TaskState.Succeeded=>task.Collaboration?.Deliverable is {} delivered?"验收完成，已交付："+delivered:"本次任务已完成，无需继续传递。",
            TaskState.Paused=>task.PauseMessage??"请打开任务决定是否继续。",
            TaskState.Failed or TaskState.TimedOut=>task.ErrorMessage??"请打开任务查看处理原因。",
            TaskState.Cancelled=>"用户已取消此交接。",
            TaskState.Acknowledged or TaskState.Running or TaskState.Dispatched=>"等待当前 Agent 写入工作回执，请勿重复投递。",
            _=>"请打开任务查看下一步；需要人工传递时，复制提示词给 "+waiting+"。"
        };
        if(task.State is not(TaskState.Succeeded or TaskState.Failed or TaskState.TimedOut or TaskState.Cancelled)&&task.CliFileTurn==task.ManualTurn)
        {
            if(task.CliFileStatus=="unknown")
            {title="CLI 投递待确认";instruction=task.CliFileError??"请先检查目标会话，确认是否收到本轮要求，不要重复投递。";}
            else if(task.CliFileStatus=="failed")
            {title="CLI 启动失败";instruction=task.CliFileError??"本轮未能启动目标 CLI，请检查配置或手动传递。";}
            else if(task.CliFileStatus=="submitting")
            {title="正在投递给 "+waiting;instruction="CLI 调用尚未返回；请等待本轮投递结果，不要重复发送。";}
        }
        var route=Name(task.RequesterId)+" → "+Name(task.RecipientId);
        if(task.Collaboration is {} collaboration&&collaboration.ReviewerId!=task.RequesterId&&collaboration.ReviewerId!=task.RecipientId)
            route+=" · 验收 "+Name(collaboration.ReviewerId);
        return new("TaskBaton · "+title,"任务 "+task.TaskId.Split('-')[0]+" · "+route+"\n"+instruction);
    }

    public async Task<object> ConfigureDesktopAsync(string executable, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new RelayException("desktop_platform", "桌面通知当前仅支持 Windows。", 3);
        var full = Path.GetFullPath(executable);
        var report = Path.Combine(Home, "desktop-probe-" + Guid.NewGuid() + ".json");
        try
        {
            var result = await RelayProcess.RunAsync(full, ["--probe", "--report-path", report], null, TimeSpan.FromSeconds(20), cancellationToken);
            if (result.TimedOut || result.ExitCode != 0 || !File.Exists(report) ||
                RelayJson.Read<ResumeResult>(File.ReadAllText(report)) is not { State: "supported", Detail: "agent-relay-desktop:1" })
                throw new RelayException("desktop_verification_failed", "通知辅助程序未通过能力验证：" + result.Error, 3);
        }
        finally { File.Delete(report); }
        store.Write(s => s.Execute("INSERT INTO desktop VALUES(1,$exe) ON CONFLICT(id) DO UPDATE SET executable=excluded.executable", ("$exe", full)));
        return new { executable = full, status = "configured", registration = "on_first_notification" };
    }

    public void DisableDesktop() => store.Write(s => s.Execute("DELETE FROM desktop"));
}

public sealed record DesktopNotificationContent(string Title,string Message);
