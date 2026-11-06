using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AgentRelay.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

public static class RelayProcess
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        string? input, TimeSpan timeout, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null,
        Action<string>? onLine = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        start.StandardOutputEncoding = new System.Text.UTF8Encoding(false);
        start.StandardErrorEncoding = new System.Text.UTF8Encoding(false);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["AMQ_NO_UPDATE_CHECK"] = "1";
        // Relay always supplies its own root and identity, independent of a parent agent shell.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("AM_", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = new Process { StartInfo = start };
        process.Start();
        // A streaming caller needs each record while the turn is still running (e.g. the session id).
        var output = onLine is null ? process.StandardOutput.ReadToEndAsync() : ReadLinesAsync(process.StandardOutput, onLine);
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new ProcessResult(-1, await output, await error, true);
        }
        return new ProcessResult(process.ExitCode, await output, await error, false);
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        var text = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            text.AppendLine(line);
            try { onLine(line); }
            catch (Exception error) when (error is InvalidOperationException or IOException or JsonException) { }
        }
        return text.ToString();
    }
}

public sealed class AmqCliAdapter(string executable, string? expectedVersion = null) : IDeliveryAdapter
{
    public string Id => "amq-cli";

    public async Task<DeliveryResult> DeliverAsync(TaskRecord task, string home, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (expectedVersion is not null)
        {
            var version = await RelayProcess.RunAsync(executable, ["--version"], null, timeout, cancellationToken);
            if (version.TimedOut || version.ExitCode != 0 || version.Output.Trim() != expectedVersion)
                return new DeliveryResult("rejected", null, "AMQ 版本已改变或无法确认，请重新 adapter verify。");
        }
        var root = Path.Combine(home, "transport", "amq", task.TaskId, task.AttemptId);
        if (!File.Exists(Path.Combine(root, "meta", "config.json")))
        {
            var init = await RelayProcess.RunAsync(executable,
                ["init", "--root", root, "--agents", $"{task.RequesterId},{task.RecipientId}"], null, timeout, cancellationToken);
            if (init.TimedOut || init.ExitCode != 0)
                return new DeliveryResult("rejected", null, "AMQ 队列初始化失败：" + init.Error, Retryable: true);
        }
        var payload = $"Agent Relay task={task.TaskId} attempt={task.AttemptId}\nRequest: {task.RequestPath}\nResult: {task.ResultPath}\n显式 ack/start/complete/fail；文件变化不表示完成。";
        var sent = await RelayProcess.RunAsync(executable,
            ["send", "--root", root, "--me", task.RequesterId, "--to", task.RecipientId,
                "--thread", "relay/" + task.TaskId, "--body", "-", "--json"], payload, timeout, cancellationToken);
        if (sent.TimedOut) return new DeliveryResult("unknown", null, "AMQ send 超时，可能已经提交。");
        // Once send starts, a nonzero exit may occur after publishing. Never infer non-delivery from exit alone.
        if (sent.ExitCode != 0) return new DeliveryResult("unknown", null, "AMQ send 返回错误，无法证明未提交：" + sent.Error);
        try
        {
            using var parsed = JsonDocument.Parse(sent.Output);
            var receipt = parsed.RootElement;
            if (receipt.ValueKind != JsonValueKind.Object ||
                !receipt.TryGetProperty("id", out var messageId) || messageId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(messageId.GetString()) ||
                !receipt.TryGetProperty("to", out var targets) || targets.ValueKind != JsonValueKind.Array ||
                !targets.EnumerateArray().Any(target => target.ValueKind == JsonValueKind.String && target.GetString() == task.RecipientId))
                return new DeliveryResult("unknown", null, "AMQ 未返回可识别的提交凭据。");
        }
        catch (JsonException)
        {
            return new DeliveryResult("unknown", null, "AMQ 输出格式无法验证，需人工检查队列。");
        }
        return new DeliveryResult("accepted", sent.Output, null);
    }

    public static async Task<AdapterRecord> VerifyAsync(string executable, string home, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(executable);
        if (!File.Exists(full)) throw new RelayException("adapter_missing", "AMQ 可执行文件不存在。", 3);
        var timeout = TimeSpan.FromSeconds(30);
        var version = await RelayProcess.RunAsync(full, ["--version"], null, timeout, cancellationToken);
        if (version.TimedOut || version.ExitCode != 0 || !Version.TryParse(version.Output.Trim(), out _))
            throw new RelayException("adapter_verification_failed", "无法确认 AMQ 版本。", 3);
        var root = Path.Combine(home, "validation", Guid.NewGuid().ToString());
        var init = await RelayProcess.RunAsync(full, ["init", "--root", root, "--agents", "relay-a,relay-b"], null, timeout, cancellationToken);
        var marker = "relay-verification-中文-" + Guid.NewGuid();
        var send = init.ExitCode == 0 && !init.TimedOut
            ? await RelayProcess.RunAsync(full, ["send", "--root", root, "--me", "relay-a", "--to", "relay-b", "--body", "-", "--json"], marker, timeout, cancellationToken)
            : throw new RelayException("adapter_verification_failed", "AMQ init 验证失败：" + init.Error, 3);
        var drain = await RelayProcess.RunAsync(full, ["drain", "--root", root, "--me", "relay-b", "--include-body", "--json"], null, timeout, cancellationToken);
        if (send.TimedOut || send.ExitCode != 0 || drain.TimedOut || drain.ExitCode != 0 || !drain.Output.Contains(marker, StringComparison.Ordinal))
            throw new RelayException("adapter_verification_failed", "AMQ send/drain 往返验证失败：" + send.Error + drain.Error, 3);
        return new AdapterRecord("amq-cli", full, version.Output.Trim(), "passed", DateTimeOffset.UtcNow,
            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; isolated init/send/drain; root={root}; queue_transport only");
    }
}

public sealed partial class RelayService
{
    public async Task<AdapterRecord> VerifyAmqAsync(string executable, CancellationToken cancellationToken = default)
    {
        var adapter = await AmqCliAdapter.VerifyAsync(executable, Home, cancellationToken);
        return store.Write(s =>
        {
            s.Execute("INSERT INTO adapters VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                ("$id", adapter.AdapterId), ("$json", RelayJson.Write(adapter)));
            return adapter;
        });
    }

    public List<AdapterRecord> Adapters() => store.Read(s => s.Query("SELECT json FROM adapters ORDER BY id", r => RelayJson.Read<AdapterRecord>(r.GetString(0))));

    public async Task DeliverPendingAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var settings = Settings();
        while (true)
        {
            var claimed = store.Write(s =>
            {
                var task = s.Task(taskId);
                var box = Outbox(s, taskId);
                if (box is null || box.State != "pending" || Terminal(task.State) || task.DeadlineAt <= Now) return null;
                RequireDispatchAccess(s, task.RequesterId, task.RecipientId, task.Mode, task.AdapterId);
                if (!Available(s, task.AdapterId))
                {
                    task.State = TaskState.AwaitingManual; s.Save(task);
                    s.Execute("UPDATE outbox SET state='manual' WHERE task_id=$id", ("$id", taskId));
                    s.Event(task, "task.manual_required", "relay", Now, "adapter unavailable");
                    return null;
                }
                if (box.Submissions >= settings.MaxDeliveryAttempts)
                {
                    task.State = TaskState.Failed; task.ErrorCode = "delivery_retries_exhausted";
                    s.Save(task); s.Event(task, "task.failed", "relay", Now, task.ErrorCode);
                    s.Execute("UPDATE outbox SET state='settled' WHERE task_id=$id", ("$id", taskId));
                    s.Notify(task, "failed", "运输重试次数已耗尽。", Now);
                    return null;
                }
                s.Execute("UPDATE outbox SET state='in_flight',submissions=submissions+1,lease_until=$lease WHERE task_id=$id",
                    ("$lease", Now.AddSeconds(settings.DeliveryTimeoutSeconds + 10).ToString("O")), ("$id", taskId));
                task.State = TaskState.Dispatching; s.Save(task);
                return task;
            });
            if (claimed is null) return;
            IDeliveryAdapter adapter;
            if (adapters.TryGetValue(claimed.AdapterId, out var injected)) adapter = injected;
            else if (claimed.AdapterId.StartsWith("cli:", StringComparison.Ordinal))
                adapter = new CliRecipeDeliveryAdapter(CliExtension(claimed.RecipientId)!, ZcodeUserDirectory);
            else
            {
                var record = store.Read(s => s.Json<AdapterRecord>("SELECT json FROM adapters WHERE id=$id", ("$id", claimed.AdapterId)))!;
                adapter = new AmqCliAdapter(record.Executable, record.Version);
            }
            DeliveryResult result;
            try
            {
                result = await adapter.DeliverAsync(claimed, Home, TimeSpan.FromSeconds(settings.DeliveryTimeoutSeconds), cancellationToken);
            }
            catch (Win32Exception exception)
            {
                // Process creation failed; no transport process accepted this attempt.
                result = new DeliveryResult("rejected", null, exception.Message);
            }
            var retry = store.Write(s =>
            {
                var task = s.Task(taskId);
                if (task.AttemptId == claimed.AttemptId && result.SidebarSync is { } sidebar &&
                    (sidebar.Status == "failed" || sidebar.WindowActivated == false))
                    s.Event(task, "zcode.sidebar_sync_failed", "relay", Now, sidebar.Message ?? sidebar.Status);
                if (task.AttemptId != claimed.AttemptId || task.State != TaskState.Dispatching) return false;
                var box = Outbox(s, taskId)!;
                var next = result.Status switch
                {
                    "accepted" => "settled",
                    "unknown" => "unknown",
                    "rejected" when result.Retryable && box.Submissions < settings.MaxDeliveryAttempts => "pending",
                    "rejected" => "settled",
                    _ => throw new InvalidOperationException("Adapter returned an invalid delivery status.")
                };
                task.State = result.Status switch
                {
                    "accepted" => TaskState.Dispatched,
                    "unknown" => TaskState.DeliveryUnknown,
                    _ => next == "pending" ? TaskState.Created : TaskState.Failed
                };
                task.ErrorCode = result.Error is null ? null : "delivery_" + result.Status;
                task.ErrorMessage = result.Error;
                s.Execute("UPDATE outbox SET state=$state,lease_until=NULL,receipt=$receipt WHERE task_id=$id",
                    ("$state", next), ("$receipt", result.Receipt), ("$id", taskId));
                s.Save(task);
                s.Event(task, result.Status switch { "accepted" => "task.dispatched", "unknown" => "task.delivery_unknown", _ => "task.delivery_rejected" }, "relay", Now, result.Error ?? "accepted");
                if (task.State is TaskState.DeliveryUnknown or TaskState.Failed)
                    s.Notify(task, "delivery_attention", "运输需要人工检查：" + result.Error, Now);
                return next == "pending";
            });
            if (!retry) return;
            var submissions = store.Read(s => Outbox(s, taskId)!.Submissions);
            await Task.Delay(TimeSpan.FromSeconds(submissions), clock, cancellationToken);
        }
    }

    private static OutboxItem? Outbox(StoreSession s, string taskId) => s.Query(
        "SELECT task_id,attempt_id,state,submissions,lease_until,receipt FROM outbox WHERE task_id=$id",
        r => new OutboxItem(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
            r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4)), r.IsDBNull(5) ? null : r.GetString(5)), ("$id", taskId)).SingleOrDefault();

    public OutboxItem? DeliveryEvidence(string taskId) => store.Read(s => Outbox(s, taskId));
}
