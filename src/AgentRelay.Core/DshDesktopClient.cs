using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentRelay.Core;

/// <summary>
/// Runs a turn inside the running DSH desktop instead of a separate headless process. The desktop composes sessions under
/// an agent preset with its own plugins, so only a turn built by the desktop process sends the same tools and system
/// prompt as the user's own turns, and switching between them keeps the provider's prompt cache.
/// The desktop host serves a loopback HTTP API on a random port. Its browser cookie is an HMAC over a small JSON body,
/// signed with the secret the desktop keeps in DSH_HOME\.credentials.yaml; Relay signs one in memory for each turn, with the
/// user's consent, and never stores or prints the secret.
/// </summary>
public static partial class DshDesktopClient
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1.5);

    /// <summary>Authenticates a read-only session-list request, without creating a session or starting an Agent turn.</summary>
    public static async Task<CliHealthCheck> CheckConnectionAsync(string executable, string dshHome,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var host = await Connect(executable, dshHome, deadline.Token);
            return new("host.desktop.connection", "pass",
                "当前 DSH 桌面本机接口认证及只读会话列表检查通过；没有创建会话、发送消息或调用模型。既有阻塞仍保留，需用户决定是否解除。" + host.Diagnostic);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new("host.desktop.connection", "fail", "DSH 桌面连接自检在 12 秒内未完成。没有发送工作请求，请确认桌面已就绪后再检查。");
        }
        catch (Exception error) when (error is DshDesktopException or HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        {
            return new("host.desktop.connection", "fail", "当前 DSH 桌面只读连接检查未通过：" + error.Message);
        }
    }

    /// <summary>Returns the turn as relay-json on stdout, like the ACP client.</summary>
    internal static async Task<ProcessResult> RunAsync(CliCommand command, string prompt, string? sessionId, string cwd,
        TimeSpan timeout, CancellationToken cancellationToken, IReadOnlyList<PreparedTaskImage> images,
        Action<string>? sessionObserved = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var dshHome = command.Environment?.GetValueOrDefault("DSH_HOME")
                ?? throw new DshDesktopException("调用方案缺少 DSH_HOME 环境变量，找不到 DSH 的数据目录。");
            using var host = await Connect(command.Executable, dshHome, deadline.Token);
            return await RunTurnAsync(host, prompt, sessionId, cwd, deadline.Token, images, sessionObserved);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(-1, "", "DSH desktop turn did not finish before the deadline.", true);
        }
        catch (Exception error) when (error is DshDesktopException or HttpRequestException or JsonException or IOException)
        {
            return new ProcessResult(1, "", error.Message, false);
        }
    }

    internal static async Task<ProcessResult> RunTurnAsync(Host host, string prompt, string? sessionId, string cwd, CancellationToken token,
        IReadOnlyList<PreparedTaskImage>? images = null, Action<string>? sessionObserved = null)
    {
        if (sessionId is null)
        {
            sessionId = (await host.Call("session/create", new JsonObject { ["cwd"] = cwd, ["agentPreset"] = "standard" }, token))
                ?["sessionId"]?.GetValue<string>() ?? throw new DshDesktopException("DSH 没有返回新会话 ID。");
            // The desktop hands out the id before the turn runs; report it now so a timeout keeps the exact conversation.
            sessionObserved?.Invoke(sessionId);
        }
        var baseline = await host.LatestSeq(sessionId, token);
        var content = new JsonArray();
        if (images is not null)
            foreach (var image in images)
                content.Add(new JsonObject { ["type"] = "image", ["mediaType"] = image.Image.MediaType,
                    ["data"] = Convert.ToBase64String(image.Data), ["name"] = image.Image.FileName });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });
        await host.Call("session/prompt", new JsonObject
        {
            ["requestId"] = Guid.NewGuid().ToString(), ["sessionId"] = sessionId, ["mode"] = "queue",
            ["content"] = content,
            // The desktop UI sends the browser's IANA zone; the same value keeps its time-context message identical.
            ["clientTimeZone"] = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var zone) ? zone : TimeZoneInfo.Local.Id
        }, token);
        try
        {
            var reply = await host.WaitForTurn(sessionId, baseline, token);
            return new ProcessResult(0, RelayJson.Write(new { session_id = sessionId, reply, accepted = true }), "", false);
        }
        catch (Exception error) when (error is DshDesktopException or HttpRequestException or JsonException or IOException)
        {
            return new ProcessResult(1, "", "DSH desktop prompt accepted; observation failed: " + error.Message, false);
        }
    }

    /// <summary>Per-request usage of one session; DSH counts inputTokens without the cached part.</summary>
    internal static async Task<List<CacheRequest>> UsageAsync(string executable, string dshHome, string sessionId, CancellationToken token)
    {
        try
        {
            using var host = await Connect(executable, dshHome, token);
            var events = await host.EventsAfter(sessionId, 0, await host.LatestSeq(sessionId, token), token);
            return [.. events.Where(e => e.Type == "assistant/message" && e.Data?["usage"] is JsonObject).Select(e =>
            {
                var usage = e.Data!["usage"]!;
                long Read(string name) => usage[name]?.GetValue<long>() ?? 0;
                return new CacheRequest(DateTimeOffset.FromUnixTimeMilliseconds(e.Time), e.Data?["model"]?.GetValue<string>(),
                    Read("inputTokens") + Read("cacheReadTokens") + Read("cacheWriteTokens"), Read("cacheReadTokens"));
            })];
        }
        catch (DshDesktopException error) { throw new RelayException("cache_usage_unreadable", error.Message); }
    }

    private static async Task<Host> Connect(string executable, string dshHome, CancellationToken token)
    {
        var credentialFile = Path.Combine(dshHome, ".credentials.yaml");
        IReadOnlyList<string> failures = [];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var processes = new Dictionary<int, DateTimeOffset>();
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    // Electron subprocesses can exit while its desktop is restarting.
                    try { processes.Add(process.Id, process.StartTime.ToUniversalTime()); }
                    catch (InvalidOperationException) { }
                }
            }
            if (processes.Count == 0)
                throw new DshDesktopException("DSH 桌面没有运行。请先打开 DSH，Relay 会在桌面里执行这一轮。");
            var listeners = await LoopbackPorts(processes, token);
            var selected = await FindHost(listeners, BrowserSessionSecret(credentialFile), credentialFile, null, token);
            if (selected.Host is not null) return selected.Host;
            failures = selected.Failures;
            // Only discovery and a read-only call are retried. No session/prompt has been submitted yet.
            if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new DshDesktopException("找到了 DSH 进程，但没有可用的本机接口。请确认 DSH 已完成启动且调用方案的 DSH_HOME 正确。" +
            (failures.Count == 0 ? "没有发现该进程的本机监听端口。" : string.Join("\n", failures)));
    }

    /// <summary>Loopback listeners owned by the given processes, from netstat (the owning process is not exposed by .NET).</summary>
    private static async Task<IReadOnlyList<DesktopListener>> LoopbackPorts(IReadOnlyDictionary<int, DateTimeOffset> processes, CancellationToken token)
    {
        var result = await RelayProcess.RunAsync("netstat", ["-ano", "-p", "TCP"], null, TimeSpan.FromSeconds(15), token);
        if (result.TimedOut || result.ExitCode != 0) throw new DshDesktopException("无法读取 DSH 的本机监听端口：" + result.Error);
        return [.. result.Output.Split('\n').Select(line => NetstatListener().Match(line))
            .Where(m => m.Success && processes.ContainsKey(int.Parse(m.Groups["pid"].Value)))
            .Select(m => new DesktopListener(int.Parse(m.Groups["port"].Value), int.Parse(m.Groups["pid"].Value),
                processes[int.Parse(m.Groups["pid"].Value)])).Distinct()];
    }

    internal sealed record DesktopListener(int Port, int ProcessId, DateTimeOffset StartedAt);

    /// <summary>Selects a listener only after its authenticated, read-only session list succeeds.</summary>
    internal static async Task<(Host? Host, IReadOnlyList<string> Failures)> FindHost(IReadOnlyList<DesktopListener> listeners,
        byte[] secret, string credentialFile, Func<DesktopListener, HttpMessageHandler>? handlerFactory, CancellationToken token)
    {
        var failures = new List<string>();
        foreach (var listener in listeners)
        {
            var host = new Host(listener, secret, credentialFile, handlerFactory?.Invoke(listener));
            var selected = false;
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(token);
            readiness.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                // The installed desktop schema calls this argument _request and returns { items: [...] }.
                // Reading the list never creates, resumes or prompts an Agent.
                var value = await host.Call("session/list", new JsonObject(), readiness.Token, "_request");
                if (value is not JsonObject list || list["items"] is not JsonArray)
                    throw new DshDesktopException("DSH session/list 返回了无法识别的结构。" + host.Diagnostic);
                selected = true;
                return (host, failures);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { failures.Add("DSH 本机接口就绪检查超时。" + host.Diagnostic); }
            catch (DshDesktopException error) { failures.Add(error.Message); }
            catch (Exception error) when (error is HttpRequestException or JsonException or IOException)
            { failures.Add(error.Message + " " + host.Diagnostic); }
            finally { if (!selected) host.Dispose(); }
        }
        return (null, failures);
    }

    [GeneratedRegex(@"^\s*TCP\s+127\.0\.0\.1:(?<port>\d+)\s+\S+\s+LISTENING\s+(?<pid>\d+)")]
    private static partial Regex NetstatListener();

    /// <summary>Reads only records → client-connection/browser-session → payload → secret; nothing else is parsed or kept.</summary>
    public static byte[] BrowserSessionSecret(string path)
    {
        if (!File.Exists(path)) throw new DshDesktopException("找不到 DSH 的凭据文件 " + path + "。");
        var lines = File.ReadAllLines(path);
        var record = Array.FindIndex(lines, l => l.TrimEnd() == "  client-connection/browser-session:");
        for (var i = record + 1; record >= 0 && i < lines.Length && (lines[i].Length == 0 || lines[i].StartsWith("   ", StringComparison.Ordinal)); i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("secret:", StringComparison.Ordinal)) continue;
            var value = line["secret:".Length..].Trim().Trim('"', '\'');
            var bytes = Base64UrlDecode(value);
            if (bytes is { Length: 32 }) return bytes;
            break;
        }
        throw new DshDesktopException("DSH 的凭据文件里没有可用的本机界面签名密钥，DSH 版本可能已改变登录方式。");
    }

    /// <summary>Why the desktop cannot be reached from Relay, or null when its signing secret is readable; for doctor.</summary>
    public static string? SecretProblem(string dshHome)
    {
        try { _ = BrowserSessionSecret(Path.Combine(dshHome, ".credentials.yaml")); return null; }
        catch (Exception error) when (error is DshDesktopException or IOException or UnauthorizedAccessException) { return error.Message; }
    }

    public static bool DesktopRunning(string executable) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).Length > 0;

    /// <summary>
    /// DSH rejects a cookie whose issuedAt is later than its own Date.now(). Allow for differences between the host and
    /// desktop clocks without exceeding the desktop's maximum cookie age.
    /// </summary>
    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(5);

    public static string Cookie(int port, byte[] secret, DateTimeOffset now)
    {
        var authority = "127.0.0.1:" + port;
        var issued = (now - ClockSkewAllowance).ToUnixTimeMilliseconds();
        var body = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { version = 1, authority, issuedAt = issued, expiresAt = issued + 3_600_000 })));
        var signature = Base64Url(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body)));
        return "dsh-auth-" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(authority))) + "=v1." + body + "." + signature;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        try { return Convert.FromBase64String(text + new string('=', (4 - text.Length % 4) % 4)); }
        catch (FormatException) { return null; }
    }

    internal sealed class Host : IDisposable
    {
        private readonly int port;
        private readonly byte[] secret;
        private readonly HttpClient http;
        internal string Diagnostic { get; }
        internal int Port => port;

        internal Host(DesktopListener listener, byte[] secret, string credentialFile, HttpMessageHandler? handler = null)
        {
            port = listener.Port; this.secret = secret;
            http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.BaseAddress = new Uri("http://127.0.0.1:" + port + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
            Diagnostic = $"[pid={listener.ProcessId}; started={listener.StartedAt:O}; port={port}; DSH_HOME={Path.GetDirectoryName(credentialFile)}; credentials_modified={File.GetLastWriteTimeUtc(credentialFile):O}]";
        }

        public async Task<JsonNode?> Call(string method, JsonObject request, CancellationToken token, string argument = "request")
        {
            var message = new JsonObject
            {
                ["type"] = "client-request", ["rpcId"] = Guid.NewGuid().ToString(), ["method"] = method,
                ["payload"] = new JsonObject { ["args"] = new JsonObject { [argument] = request } }
            };
            using var content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
            using var call = new HttpRequestMessage(HttpMethod.Post, "api/" + method) { Content = content };
            call.Headers.Add("Cookie", Cookie(port, secret, DateTimeOffset.UtcNow));
            using var response = await http.SendAsync(call, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new DshDesktopException($"DSH 桌面拒绝了 Relay 的登录（401）。操作={method}。请检查 DSH_HOME、桌面启动状态与桥接版本。若连接检查通过，可重新检测后解除阻塞。" + Diagnostic);
            response.EnsureSuccessStatusCode();
            var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject;
            if (envelope?["result"] is not JsonObject result)
                throw new DshDesktopException($"DSH {method} 返回了无法识别的 RPC 结构。" + Diagnostic);
            if (result["ok"]?.GetValue<bool>() != true && method == "session/prompt" &&
                result["error"]?["details"]?["reason"]?.GetValue<string>() == "MODEL_DOES_NOT_SUPPORT_IMAGES")
                throw new RelayException("cli_image_unsupported", "DSH 当前模型不支持图片，任务未发送。请在 DSH 中选择支持图片的模型。");
            if (result["ok"]?.GetValue<bool>() != true)
                throw new DshDesktopException($"DSH {method} 失败：{result["error"]?["code"]} {result["error"]?["message"]}" + Diagnostic);
            return result["value"];
        }

        public async Task<long> LatestSeq(string sessionId, CancellationToken token) =>
            (await Call("session/projections", new JsonObject { ["sessionId"] = sessionId }, token))?["asOfSeq"]?.GetValue<long>() ?? 0;

        /// <summary>Waits for the first turn that starts after the baseline to end, and returns its last assistant text.</summary>
        public async Task<string> WaitForTurn(string sessionId, long baseline, CancellationToken token)
        {
            while (true)
            {
                await Task.Delay(Poll, token);
                var latest = await LatestSeq(sessionId, token);
                if (latest <= baseline) continue;
                var events = await EventsAfter(sessionId, baseline, latest, token);
                var start = events.FirstOrDefault(e => e.Type == "turn/start");
                if (start is null) continue;
                var turn = start.Data?["turn"]?.GetValue<long>();
                var end = events.FirstOrDefault(e => e.Type == "turn/end" && e.Data?["turn"]?.GetValue<long>() == turn);
                if (end is null) continue;
                var reason = end.Data?["reason"]?["kind"]?.GetValue<string>();
                if (reason != "completed") throw new DshDesktopException("DSH 这一轮没有正常结束：" + reason);
                return events.Where(e => e.Type == "assistant/message" && e.Data?["turn"]?.GetValue<long>() == turn)
                    .Select(e => string.Concat((e.Data?["message"]?["content"]?.AsArray() ?? [])
                        .Where(c => c?["type"]?.GetValue<string>() == "text").Select(c => c!["text"]!.GetValue<string>())))
                    .LastOrDefault(text => text.Length > 0) ?? "";
            }
        }

        public async Task<List<SessionEvent>> EventsAfter(string sessionId, long baseline, long through, CancellationToken token)
        {
            var events = new List<SessionEvent>();
            long? before = null;
            while (true)
            {
                var request = new JsonObject
                {
                    ["address"] = new JsonObject { ["kind"] = "session", ["sessionId"] = sessionId },
                    ["throughSeq"] = through, ["maxMessages"] = 200
                };
                if (before is not null) request["beforeSeq"] = before;
                var page = await Call("session/page", request, token);
                var records = (page?["records"]?.AsArray() ?? []).Select(r => r?["event"]).OfType<JsonNode>()
                    .Select(e => new SessionEvent(e["type"]?.GetValue<string>() ?? "", e["seq"]?.GetValue<long>() ?? 0,
                        (long)(e["time"]?.GetValue<double>() ?? 0), e["data"])).ToList();
                events.AddRange(records.Where(e => e.Seq > baseline));
                if (page?["hasMore"]?.GetValue<bool>() != true || records.Count == 0 || records.Min(e => e.Seq) <= baseline) break;
                before = records.Min(e => e.Seq);
            }
            return [.. events.OrderBy(e => e.Seq)];
        }

        public void Dispose() => http.Dispose();
    }

    internal sealed record SessionEvent(string Type, long Seq, long Time, JsonNode? Data);

    private sealed class DshDesktopException(string message) : Exception(message);
}
