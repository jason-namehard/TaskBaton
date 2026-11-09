using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.ComponentModel;

return await RelayCli.RunAsync(args);

public static class RelayCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("Agent Relay — 本机 Agent 任务交接与恢复");
        var home = new Option<string>("--home") { Description = "Relay 数据根目录；也可设 RELAY_HOME", Recursive = true };
        root.Options.Add(home);
        root.Options.Add(new Option<bool>("--json") { Description = "以 JSON 输出（操作命令默认已使用 JSON）", Recursive = true });

        void Add(Command parent, string name, string description, string[] specs, Func<RelayService, Values, Task<object>> action)
        {
            var command = new Command(name, description);
            var options = new Dictionary<string, Option<string>>();
            var flags = new Dictionary<string, Option<bool>>();
            foreach (var spec in specs)
            {
                var key = spec.TrimEnd('!', '?');
                if (spec.EndsWith('?'))
                {
                    var flag = new Option<bool>("--" + key); flags.Add(key, flag); command.Options.Add(flag);
                }
                else
                {
                    var option = new Option<string>("--" + key) { Required = spec.EndsWith('!') };
                    options.Add(key, option); command.Options.Add(option);
                }
            }
            command.SetAction(async (parse, cancellationToken) =>
            {
                void Emit(object value)
                {
                    if (options.TryGetValue("report-path", out var reportOption) && parse.GetValue(reportOption) is { } path)
                        File.WriteAllText(path, RelayJson.Write(value), new System.Text.UTF8Encoding(false));
                    else Print(value);
                }
                int Fail(string errorCode, string message, int exitCode)
                {
                    if (options.TryGetValue("error-message-path", out var errorOption) && parse.GetValue(errorOption) is { } errorPath)
                        File.WriteAllText(errorPath, message, new System.Text.UTF8Encoding(false));
                    return Error(errorCode, message, exitCode, Emit);
                }
                try
                {
                    var directory = parse.GetValue(home) ?? Environment.GetEnvironmentVariable("RELAY_HOME")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentRelay");
                    var service = new RelayService(directory);
                    var result = await action(service, new Values(parse, options, flags, cancellationToken));
                    var code = result switch
                    {
                        TaskView { Task.ErrorCode: "execution_unknown" } when name is "run" or "dispatch" => 6,
                        TaskView { Task.State: TaskState.DeliveryUnknown } when name is "dispatch" or "approve" or "retry" => 6,
                        TaskView { Task.State: TaskState.AwaitingManual or TaskState.AwaitingConfirmation } when name is "dispatch" or "approve" or "retry" => 4,
                        TaskView { Task.State: TaskState.Failed } when name is "dispatch" or "approve" or "retry" => 1,
                        AgentRecord { ValidationStatus: "invalid" } => 3,
                        _ => 0
                    };
                    var view = result as TaskView;
                    object? operationError = code == 1 && view is not null
                        ? new { code = view.Task.ErrorCode, message = view.Task.ErrorMessage } : null;
                    Emit(new { relay_version = 1, ok = code is 0 or 4 or 6, task_id = view?.Task.TaskId,
                        state = view?.Task.State, next_action = view?.NextAction, data = result, error = operationError });
                    return code;
                }
                catch (RelayException error) { return Fail(error.Code, error.Message, error.ExitCode); }
                catch (IOException error) { return Fail("file_error", error.Message, 1); }
                catch (UnauthorizedAccessException error) { return Fail("access_denied", error.Message, 3); }
                catch (JsonException error) { return Fail("invalid_json", error.Message, 2); }
                catch (ArgumentException error) { return Fail("invalid_argument", error.Message, 2); }
                catch (SqliteException error) { return Fail("database_error", error.Message, 1); }
                catch (Win32Exception error) { return Fail("process_start_failed", error.Message, 3); }
            });
            parent.Subcommands.Add(command);
        }
        void Sync(Command parent, string name, string description, string[] specs, Func<RelayService, Values, object> action) =>
            Add(parent, name, description, specs, (service, values) => Task.FromResult(action(service, values)));
        Command Group(string name, string description)
        {
            var command = new Command(name, description); root.Subcommands.Add(command); return command;
        }

        Sync(root, "init", "创建独立数据目录和 SQLite 账本", [], (s, _) => { s.Initialize(); return new { home = s.Home }; });
        var uninstall = Group("uninstall", "卸载时检查和清除 Relay 数据");
        Sync(uninstall, "plan", "查询当前站点，供卸载程序显示", ["location-file!", "output!"], (s, v) =>
        {
            var directory = UninstallCleanup.ResolveHome(v.Get("location-file"), v.Parse.GetValue(home) ?? Environment.GetEnvironmentVariable("RELAY_HOME"));
            File.WriteAllText(v.Get("output"), directory, new System.Text.UTF8Encoding(false));
            return new { home = directory };
        });
        Sync(uninstall, "cleanup", "移除已登记的规则并按确认清理站点配置", ["location-file!", "expected-home!", "remove-rules?", "remove-data?", "report-path", "error-message-path"], (s, v) =>
        {
            if (Process.GetProcessesByName("AgentRelay.Desktop").Length > 0)
                throw new RelayException("uninstall_app_running", "请先从托盘退出 Agent Relay，再重新执行数据清理。", 3);
            return new { rules_removed = UninstallCleanup.Apply(v.Get("location-file"), v.Parse.GetValue(home) ?? Environment.GetEnvironmentVariable("RELAY_HOME"),
                v.Get("expected-home"), v.Flag("remove-rules"), v.Flag("remove-data")) };
        });
        var agent = Group("agent", "Agent 注册与只读发现");
        Sync(agent, "discover", "列出候选路径；不登记、不修改配置", [], (s, _) => s.Discover());
        Sync(agent, "list", "列出已登记 Agent", [], (s, _) => s.Agents());
        Add(agent, "models", "列出 Relay 新建会话时可用的模型与思考强度，以及当前默认；续接的会话沿用自己的模型", ["agent!"],
            async (s, v) => await s.AgentModelsAsync(v.Get("agent"), v.CancellationToken));
        Add(agent, "set-model", "设置 Relay 新建会话时的默认模型与思考强度（写入宿主自己的配置）", ["agent!", "provider!", "model!", "reasoning"],
            async (s, v) => await s.SetAgentModelAsync(v.Get("agent"), new(v.Get("provider"), v.Get("model"), v.Optional("reasoning")), v.CancellationToken));
        Sync(agent, "cache-check", "读取宿主记录的逐次请求用量，对照桌面与 Relay 轮次的缓存命中；不调用模型", ["agent!", "session!", "limit"],
            (s, v) => s.CacheUsage(v.Get("agent"), v.Get("session"),
                v.Optional("limit") is { } limit && int.TryParse(limit, out var count) ? count : 30));
        var skill = Group("skill", "Relay 自带的 Agent 技能");
        Sync(skill, "list", "列出自带技能及其在 Relay 数据目录中的路径", [], (s, _) => RelayService.SkillNames.Select(s.Skill));
        Sync(skill, "install", "把技能复制到 Agent 自己的技能目录（如 ~/.claude/skills）", ["name!", "target!"],
            (s, v) => s.InstallSkill(v.Get("name"), v.Get("target")));
        Sync(agent, "register", "登记或手工修正路径（需要重新 validate）",
            ["id!", "host", "config-path!", "cli-path", "host-version", "instruction-path"],
            (s, v) => s.Register(v.Get("id"), v.Optional("host") ?? "manual", v.Get("config-path"),
                v.Optional("cli-path"), v.Optional("host-version"), v.Optional("instruction-path") is { } path ? [path] : []));
        Sync(agent, "setup", "从 JSON 填写节点信息；不执行宿主或安装桥接", ["manifest!", "id"],
            (s, v) => { s.Initialize(); return s.SetupAgentFromJson(File.ReadAllText(v.Get("manifest")), v.Optional("id")); });
        Sync(agent, "setup-prompt", "说明 Agent 如何调查并填写自己的节点信息", ["id"],
            (s, v) => new { prompt = s.AgentSetupPrompt(v.Optional("id")) });
        Sync(agent, "validate", "验证已登记路径存在、可读；不启动宿主", ["id!"], (s, v) => s.ValidateAgent(v.Get("id")));
        Add(agent, "verify-executor", "验证 Codex 原生 CLI 版本及非交互契约，不调用模型", ["id!"],
            async (s, v) => await s.VerifyExecutorAsync(v.Get("id"), v.CancellationToken));
        Sync(agent, "executors", "显示已验证的执行器", [], (s, _) => s.Executors());
        Add(agent, "verify-resumer", "验证精确会话继续接口；exec-resume 会启动模型轮次", ["id!", "kind!"],
            async (s, v) => await s.VerifyResumerAsync(v.Get("id"), v.Get("kind"), v.CancellationToken));
        Sync(agent, "resumers", "显示已验证的会话继续适配器", [], (s, _) => s.Resumers());

        var session = Group("session", "只查询指定 Agent 的会话，并管理任务的精确绑定");
        Sync(session,"capabilities","显示调用方案声明的能力；不代表真实调用已验证",["agent!"],(s,v)=>s.SessionCapabilities(v.Get("agent")));
        Add(session, "query", "定向查询会话；不调用模型、不自动选择", ["agent!", "refresh?"],
            async (s,v) => await s.QuerySessionsAsync(v.Get("agent"),v.Flag("refresh"),v.CancellationToken));
        Sync(session, "source", "配置指定 Agent 的会话发现来源", ["agent!", "provider!", "directory!"],
            (s,v) => s.ConfigureSessionDiscovery(v.Get("agent"),v.Get("provider"),v.Get("directory")));
        Sync(session, "bindings", "查看任务与会话的持久绑定", ["task!"], (s,v) => s.SessionBindings(v.Get("task")));
        Sync(session, "bind", "用户明确输入已有会话 ID；执行前仍验证宿主记录", ["task!", "agent!", "session!", "cwd", "profile", "session-path"],
            (s,v) => s.BindManualTaskSession(v.Get("task"),v.Get("agent"),v.Get("session"),v.Optional("cwd"),v.Optional("profile"),v.Optional("session-path")));
        Sync(session, "new", "明确为任务选择新会话；不立即调用模型", ["task!", "agent!"],
            (s,v) => { s.UseNewTaskSession(v.Get("task"),v.Get("agent")); return new {status="new_session_selected"}; });
        var handoff = Group("handoff", "创建文件交接；来源身份来自调用适配器上下文");
        Add(handoff,"cli-create","直接提交工作要求并绑定会话；发送会启动目标 CLI",["manifest!","send?","approve?"],async(s,v)=>
        {
            if(v.Flag("send")&&!v.Flag("approve"))throw new RelayException("approval_required","真实 CLI 投递可能产生模型费用，确认后加 --approve。");
            var request=RelayJson.Read<CliHandoffRequest>(File.ReadAllText(v.Get("manifest")));
            var created=s.CreateCliHandoff(request);
            return v.Flag("send")?await s.SendFileHandoffAsync(created.Task.TaskId,s.FileHandoffPrompt(created.Task),v.CancellationToken):created;
        });
        Add(handoff,"send","按已绑定会话与启用方向发送现有 CLI 文件任务",["task!","approve?"],async(s,v)=>
        {
            if(!v.Flag("approve"))throw new RelayException("approval_required","真实 CLI 投递可能产生模型费用，确认后加 --approve。");
            var task=s.Status(v.Get("task")).Task;
            return await s.SendFileHandoffAsync(task.TaskId,s.FileHandoffPrompt(task),v.CancellationToken,expectedTurn:task.ManualTurn);
        });
        Sync(handoff, "create", "读取短清单，不要求 Agent 查找会话 ID", ["manifest!"],
            (s,v) => s.CreateFileHandoffFromContext(ManualTransfers.Parse(File.ReadAllText(v.Get("manifest")),s.Agents())));
        Sync(handoff, "configure", "首次投递前设置持续修改与验收、单轮时限和自动投递", ["task!", "author", "criteria", "max-rounds", "turn-timeout-minutes", "auto-deliver?"],
            (s,v) => s.ConfigureFileHandoff(v.Get("task"), new(v.Optional("author") is { } author
                ? new CollaborationOptions(author, v.Optional("criteria"), v.Number("max-rounds", 5)) : null,
                v.Flag("auto-deliver"), v.Optional("turn-timeout-minutes") is null ? null : v.Number("turn-timeout-minutes", 0))));
        Sync(handoff, "relay-plan", "设置轻量定次接力：--steps \"b=实现;a:test=测试;b:modify=按报告修改\"", 
            ["task!", "steps!", "turn-timeout-minutes", "independent-acceptance?", "new-session-each-turn?", "auto-deliver?"],
            (s,v) => s.ConfigureRelayPlan(v.Get("task"), new RelayPlanOptions(RelayService.ParseSteps(v.Get("steps")),
                v.Flag("independent-acceptance"), v.Optional("turn-timeout-minutes") is null ? null : v.Number("turn-timeout-minutes", 0),
                v.Flag("auto-deliver"), v.Flag("new-session-each-turn"))));
        Sync(handoff, "status", "由当前 Agent 轻量登记 received 或 working；不必手写回执 JSON", ["task!", "status!", "note"],
            (s,v) => s.RegisterTurnStatus(v.Get("task"), v.Get("status"), v.Optional("note")));
        Sync(handoff, "allow-images", "只为指定后续轮次补带所选图片（省略 --image 表示全部原图）", ["task!", "turn", "image"],
            (s,v) =>
            {
                var taskId = v.Get("task");
                var turn = v.Optional("turn") is not { } value ? s.Status(taskId).Task.ManualTurn
                    : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number
                    : throw new ArgumentException("--turn 必须为整数。");
                return s.AllowTurnImages(taskId, turn,
                    v.Optional("image")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            });
        Add(handoff, "sync", "导入回执；加 --deliver 时发送已选择自动投递且能力已验证的轮次", ["deliver?", "approve?"], async (s,v) =>
        {
            if(v.Flag("deliver")&&!v.Flag("approve"))throw new RelayException("approval_required","自动投递会启动目标 CLI，可能产生模型费用，确认后加 --approve。");
            return await s.SyncFileHandoffsAsync(v.Flag("deliver"),v.CancellationToken);
        });
        Sync(handoff, "progress", "当前处理者、阶段、投递方式与是否在等用户", ["task!"],
            (s,v) => s.FileHandoffProgress(s.Status(v.Get("task")).Task));
        Sync(handoff, "continue", "用户决定继续已暂停的交接；轮数用完时须追加轮数", ["task!", "note", "extra-rounds"],
            (s,v) => s.ContinueFileHandoff(v.Get("task"), v.Optional("note"), v.Number("extra-rounds", 0)));
        Sync(handoff, "allow-resend", "确认本轮未送达后允许手动再发送一次", ["task!", "reason!", "approve?"],
            (s,v) => s.AllowFileHandoffResend(v.Get("task"), v.Get("reason"), v.Flag("approve")));

        var cliRecipe = Group("cli-recipe", "保存已有 CLI 调用方案，不执行或启用");
        Sync(cliRecipe, "import", "从 JSON 保存指定 Agent 的调用方案", ["agent!", "manifest!"],
            (s, v) => s.ImportCliRecipe(v.Get("agent"), File.ReadAllText(v.Get("manifest"))));
        Sync(cliRecipe, "remove", "删除指定 Agent 的调用方案及其验证记录", ["agent!"],
            (s, v) => { s.RemoveCliExtension(v.Get("agent")); return new { status = "removed" }; });
        Sync(cliRecipe, "confirm", "记录用户已在宿主中看到探针会话（--visible 表示可见，省略表示撤销）", ["agent!", "probe!", "visible?"],
            (s, v) => s.ConfirmCliProbe(v.Get("agent"), v.Get("probe"), v.Flag("visible")));
        Add(cliRecipe, "doctor", "检查本机配置与 DSH 只读接口连接，不调用模型、不解除阻塞", ["agent!"],
            async (s, v) => await s.CliHealthAsync(v.Get("agent"),v.CancellationToken));
        Sync(cliRecipe, "unblock", "用户已处理登录、模型或权限问题后，解除自动投递阻塞", ["agent!"],
            (s, v) => s.ClearCliBlock(v.Get("agent")));
        Add(cliRecipe, "probe", "发起一次极短真实 CLI 探针（含写文件检查），可能产生模型费用", ["agent!", "operation!", "approve?"], async (s, v) =>
        {
            if (!v.Flag("approve")) throw new RelayException("approval_required", "真实探针可能产生模型费用；明确同意后加 --approve。");
            return await s.RunCliProbeAsync(v.Get("agent"), v.Get("operation"), v.CancellationToken);
        });

        var guidance = Group("guidance", "管理按需读取的 Agent 交接规则");
        Sync(guidance, "list", "列出本中转站安装的接入规则", [], (s, _) => s.GuidanceRegistrations());
        Sync(guidance, "prompt", "生成一句按需读取规则及其详细指引", ["agent!"],
            (s, v) => new { rule = s.GuidancePrompt(v.Get("agent")), instructions = s.StableInstructionPath(v.Get("agent")) });
        Sync(guidance, "preview", "预览将加入指定指令文件的一句规则", ["agent!", "file!"],
            (s, v) => s.PreviewGuidance(v.Get("agent"), v.Get("file")));
        Sync(guidance, "install", "用户明确批准后写入带标记的一句规则", ["agent!", "file!", "approve?"], (s, v) =>
        {
            if (!v.Flag("approve")) throw new RelayException("approval_required", "请先向用户说明指令文件和规则内容；确认后加 --approve。");
            return s.InstallGuidance(v.Get("agent"), v.Get("file"));
        });
        Sync(guidance, "remove", "只移除 Relay 自己的规则，保留其他内容", ["agent", "all?"], (s, v) =>
        {
            var id = v.Optional("agent");
            if (v.Flag("all") == (id is not null)) throw new RelayException("invalid_arguments", "指定 --agent 或 --all 中的一项。");
            return new { removed = v.Flag("all") ? s.RemoveAllGuidance() : s.RemoveGuidance(id!) };
        });
        Sync(guidance, "refresh", "刷新稳定入口中的节点目录和当前中转站位置", [], (s, _) =>
        { s.RefreshGuidance(); return new { status = "updated" }; });

        var permission = Group("permission", "有向通信权限，未配置默认拒绝");
        Sync(permission, "set", "只改变指定方向", ["from!", "to!", "decision!"], (s, v) => s.SetPermission(v.Get("from"), v.Get("to"), v.Get("decision")));
        Sync(permission, "list", "显示显式权限", [], (s, _) => s.Permissions());

        var settings = Group("settings", "Relay 自身配置");
        Sync(settings, "show", "显示当前保留和时限配置", [], (s, _) => s.Settings());
        Sync(settings, "set", "更新新任务的默认配置", ["ttl-days", "task-timeout-minutes", "delivery-timeout-seconds", "max-delivery-attempts"],
            (s, v) =>
            {
                var current = s.Settings();
                return s.Configure(new RelaySettings(v.Number("ttl-days", current.ArtifactTtlDays), v.Number("task-timeout-minutes", current.TaskTimeoutMinutes),
                    v.Number("delivery-timeout-seconds", current.DeliveryTimeoutSeconds), v.Number("max-delivery-attempts", current.MaxDeliveryAttempts)));
            });

        var adapter = Group("adapter", "显式验证可信的外部适配器");
        Sync(adapter, "inspect-config", "只读盘点指定 JSON/TOML 的 MCP 命令；不显示 env 值或执行命令", ["path!", "format!"],
            (s, v) => s.InspectConfiguration(v.Get("path"), v.Get("format")));
        Sync(adapter, "sources", "列出迁移登记的来源、版本和声明能力", [], (s, _) => s.AdapterSources());
        Sync(adapter, "list", "查看验证记录", [], (s, _) => s.Adapters());
        Add(adapter, "verify", "在隔离目录运行 AMQ init/send/drain，不启动 AI", ["id!", "executable!"], async (s, v) =>
        {
            if (v.Get("id") != "amq-cli") throw new RelayException("unsupported_adapter", "当前只支持 amq-cli。");
            return await s.VerifyAmqAsync(v.Get("executable"), v.CancellationToken);
        });

        var migration = Group("migration", "配置迁移预览、批准、验证与回滚");
        Sync(migration, "plan", "按唯一精确片段生成提案；不修改目标配置", ["manifest!"], (s, v) =>
        {
            var options = new JsonSerializerOptions(RelayJson.Options) { RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
            var request = JsonSerializer.Deserialize<MigrationRequest>(File.ReadAllText(v.Get("manifest")), options)
                ?? throw new JsonException("Expected migration manifest.");
            var plan = s.PlanMigration(request);
            return new { plan, preview = File.ReadAllText(plan.PreviewPath) };
        });
        Sync(migration, "show", "展示已保存的精确提案与备份位置", ["id!"], (s, v) =>
        {
            var plan = s.Migration(v.Get("id")); return new { plan, preview = File.ReadAllText(plan.PreviewPath) };
        });
        Sync(migration, "list", "列出迁移状态", [], (s, _) => s.Migrations());
        Sync(migration, "apply", "审阅后明确批准应用；配置变化则停止", ["id!", "approve?", "reason"],
            (s, v) => s.ApplyMigration(v.Get("id"), v.Flag("approve"), v.Optional("reason") ?? ""));
        Sync(migration, "validate", "以该 route-id 的派发、完成和继续证据验证新入口", ["id!", "task!"],
            (s, v) => s.ValidateMigration(v.Get("id"), v.Get("task")));
        Sync(migration, "rollback", "明确批准回滚；后续修改不能被备份覆盖", ["id!", "approve?", "reason"],
            (s, v) => s.RollbackMigration(v.Get("id"), v.Flag("approve"), v.Optional("reason") ?? ""));
        Sync(migration, "recover", "只核对中断的应用/回滚状态，不自动写目标配置", ["id!"], (s, v) => s.RecoverMigration(v.Get("id")));
        Sync(migration, "audit", "迁移批准、应用、验证与回滚历史", ["id!"], (s, v) => s.MigrationAudit(v.Get("id")));

        var task = Group("task", "任务派发、显式回调与恢复");
        Add(task, "dispatch", "保存交接并立即返回；execute 启动有界单次 worker", ["from!", "to!", "request!", "mode", "adapter", "idempotency-key!", "deadline-at", "origin-session", "execute?", "execution-timeout-seconds"], async (s, v) =>
        {
            OriginSession? origin = v.Optional("origin-session") is { } path ? RelayJson.Read<OriginSession>(File.ReadAllText(path)) : null;
            var executionTimeout = v.ExecutionTimeout("execution-timeout-seconds");
            if (v.Flag("execute"))
            {
                if (v.Optional("mode") is { } mode && mode != "auto")
                    throw new RelayException("invalid_execution_mode", "dispatch --execute 仅用于 auto；confirm 模式请在 approve 时显式使用 --execute。");
                s.RequireExecutorReady(v.Get("to"));
            }
            var dispatched = await s.DispatchAsync(new DispatchRequest(v.Get("from"), v.Get("to"), File.ReadAllText(v.Get("request")),
                v.Optional("mode") ?? "auto", v.Optional("adapter") ?? (v.Flag("execute") ? "amq-cli" : s.PreferredDeliveryAdapter(v.Get("to"))), v.Get("idempotency-key"), v.Timestamp("deadline-at"), origin), v.CancellationToken);
            if (v.Flag("execute") && dispatched.Task.State == TaskState.Dispatched)
                LaunchExecution(s, dispatched.Task, executionTimeout);
            return s.Status(dispatched.Task.TaskId);
        });
        Add(task, "run", "领取一次 AMQ 任务并运行已验证 Codex；不轮询", ["task!", "attempt!", "sender!", "timeout-seconds", "report-path"],
            async (s, v) => await s.RunTaskAsync(v.Get("task"), v.Get("attempt"), v.Get("sender"),
                v.ExecutionTimeout("timeout-seconds"), cancellationToken: v.CancellationToken));
        Sync(task, "execution", "只读显示执行、会话和 worker 证据", ["task!", "attempt"],
            (s, v) => (object?)s.ExecutionEvidence(v.Get("task"), v.Optional("attempt")) ?? new { message = "尚无执行记录。" });
        Sync(task, "status", "只读状态，不触发投递或时限处理", ["task!"], (s, v) => s.Status(v.Get("task")));
        Sync(task, "delete", "删除任务及其历史记录；文件保留，已在运行的 Agent 不会被停止", ["task!", "approve?"],
            (s, v) => { s.DeleteTask(v.Get("task"), v.Flag("approve")); return new { status = "deleted" }; });
        Sync(task, "list", "只读列出任务", [], (s, _) => s.Tasks());
        Add(task, "approve", "批准待确认任务；可显式启动 worker", ["task!", "execute?", "execution-timeout-seconds"], async (s, v) =>
        {
            var executionTimeout = v.ExecutionTimeout("execution-timeout-seconds");
            if (v.Flag("execute")) s.RequireExecutorReady(s.Status(v.Get("task")).Task.RecipientId);
            var approved = await s.ApproveAsync(v.Get("task"), v.CancellationToken);
            if (v.Flag("execute") && approved.Task.State == TaskState.Dispatched)
                LaunchExecution(s, approved.Task, executionTimeout);
            return s.Status(approved.Task.TaskId);
        });
        Sync(task, "ack", "接收方确认当前尝试", ["task!", "attempt!", "sender!", "idempotency-key!"],
            (s, v) => s.Acknowledge(v.Get("task"), v.Get("attempt"), v.Get("sender"), v.Get("idempotency-key")));
        Sync(task, "start", "接收方报告开始执行", ["task!", "attempt!", "sender!", "idempotency-key!"],
            (s, v) => s.Acknowledge(v.Get("task"), v.Get("attempt"), v.Get("sender"), v.Get("idempotency-key"), start: true));
        foreach (var failure in new[] { false, true })
        {
            var isFailure = failure;
            Add(task, failure ? "fail" : "complete", failure ? "提交失败报告" : "提交成功结果；文件更新本身不表示完成",
                ["task!", "attempt!", "sender!", "idempotency-key!", "status!", "result!", "error-code", "message", "side-effects-possible?"], async (s, v) =>
                {
                    if (v.Get("status") != (isFailure ? "failed" : "succeeded")) throw new RelayException("invalid_status", "status 与回调命令不一致。");
                    s.Complete(v.Get("task"), v.Get("attempt"), v.Get("sender"), v.Get("idempotency-key"), v.Get("result"),
                        isFailure, v.Optional("error-code"), v.Optional("message"), v.Flag("side-effects-possible") ? true : null);
                    await s.ProcessContinuationsAsync(v.Get("task"), cancellationToken: v.CancellationToken);
                    return s.Status(v.Get("task"));
                });
        }
        Sync(task, "cancel", "停止 Relay 等待和后续投递，不终止宿主", ["task!", "reason!"], (s, v) => s.Cancel(v.Get("task"), v.Get("reason")));
        Add(task, "retry", "明确批准后创建新执行尝试；可能重复副作用", ["task!", "reason!", "approve?"],
            async (s, v) => await s.RetryAsync(v.Get("task"), v.Get("reason"), v.Flag("approve"), v.CancellationToken));
        Sync(task, "evidence", "只读显示运输凭据", ["task!"], (s, v) => (object?)s.DeliveryEvidence(v.Get("task")) ?? new { message = "无运输 outbox；可能为手工模式。" });
        Sync(task, "continuations", "只读显示继续提交和桌面通知证据", ["task!"], (s, v) => s.Continuations(v.Get("task")));
        Add(task, "continue", "处理尚未提交的继续消息；不会重发 submitted/unknown", ["task!", "timeout-seconds"], async (s, v) =>
        {
            await s.ProcessContinuationsAsync(v.Get("task"), timeoutSeconds: v.ExecutionTimeout("timeout-seconds"), cancellationToken: v.CancellationToken);
            return s.Status(v.Get("task"));
        });

        var artifact = Group("artifact", "登记受管产物；不会完成任务");
        Sync(artifact, "register", "把外部文件导入当前任务的受管目录", ["task!", "attempt!", "kind!", "path!"],
            (s, v) => s.RegisterArtifact(v.Get("task"), v.Get("attempt"), v.Get("kind"), v.Get("path")));
        Sync(artifact, "list", "显示产物及过期时间", ["task!"], (s, v) => s.Artifacts(v.Get("task")));
        var inbox = Group("inbox", "持久通知");
        Sync(inbox, "list", "列出等待、就绪和失败通知", [], (s, _) => s.Inbox());
        var audit = Group("audit", "事件历史");
        Sync(audit, "list", "按提交顺序显示事件", ["task"], (s, v) => s.Audit(v.Optional("task")));
        var prompt = Group("prompt", "手工交接和继续提示词");
        Sync(prompt, "show", "返回可复制提示词", ["task!"], (s, v) =>
        {
            var view = s.Status(v.Get("task"));
            return new { text = view.Task.IsFileHandoff && view.NextAction == "copy_file_handoff"
                ? s.FileHandoffPrompt(view.Task) : view.Prompt };
        });
        foreach (var operation in new[] { "copy", "open" })
        {
            var op = operation;
            Add(prompt, op, op == "copy" ? "把继续提示词复制到 Windows 剪贴板" : "打开结果与继续提示词窗口", ["task!"], async (s, v) =>
            {
                var executable = s.DesktopExecutable() ?? throw new RelayException("desktop_unconfigured", "请先 desktop configure。", 3);
                s.Status(v.Get("task"));
                if (op == "open")
                {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
                    foreach (var arg in new[] { "--open", "--home", s.Home, "--task", v.Get("task") }) start.ArgumentList.Add(arg);
                    using var process = Process.Start(start)!;
                    return new { status = "opened", task_id = v.Get("task") };
                }
                var result = await RelayProcess.RunAsync(executable, ["--copy", "--home", s.Home, "--task", v.Get("task")], null, TimeSpan.FromSeconds(15), v.CancellationToken);
                if (result.TimedOut || result.ExitCode != 0) throw new RelayException("clipboard_failed", result.Error, 3);
                return new { status = "copied", task_id = v.Get("task") };
            });
        }
        var desktop = Group("desktop", "Windows 本地通知与继续窗口");
        Add(desktop, "configure", "验证并登记通知程序；首次提醒时注册应用点击激活", ["executable!"],
            async (s, v) => await s.ConfigureDesktopAsync(v.Get("executable"), v.CancellationToken));
        Sync(desktop, "show", "显示通知程序路径", [], (s, _) => new { executable = s.DesktopExecutable() });
        Sync(desktop, "disable", "停止此数据目录的后续桌面提醒", [], (s, _) => { s.DisableDesktop(); return new { status = "disabled" }; });
        Add(root, "recover", "处理超时和遗留 outbox；未知投递不自动重发", ["task"], async (s, v) => await s.RecoverAsync(v.Optional("task"), v.CancellationToken));
        Sync(root, "cleanup", "仅清理过期受管产物，默认预览", ["dry-run?", "apply?"], (s, v) =>
        {
            if (v.Flag("dry-run") && v.Flag("apply")) throw new RelayException("conflicting_options", "dry-run 与 apply 不能同时使用。");
            return s.Cleanup(v.Flag("apply"));
        });

        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0) return Error("invalid_arguments", string.Join(" ", parsed.Errors.Select(e => e.Message)), 2);
        return await parsed.InvokeAsync();
    }

    private static void Print(object value) => Console.WriteLine(RelayJson.Write(value));
    private static int Error(string code, string message, int exitCode, Action<object>? emit = null)
    {
        (emit ?? Print)(new { relay_version = 1, ok = false, task_id = (string?)null, state = (string?)null,
            next_action = (string?)null, data = (object?)null, error = new { code, message } });
        return exitCode;
    }

    private static void LaunchExecution(RelayService service, TaskRecord task, int timeoutSeconds)
    {
        var report = Path.Combine(Path.GetDirectoryName(task.RequestPath)!, "worker-report.json");
        DetachedWorker.Launch(service, task, timeoutSeconds, ["--home", service.Home, "task", "run", "--task", task.TaskId,
            "--attempt", task.AttemptId, "--sender", task.RecipientId, "--timeout-seconds", timeoutSeconds.ToString(CultureInfo.InvariantCulture), "--report-path", report]);
    }

    private sealed record Values(ParseResult Parse, Dictionary<string, Option<string>> Options,
        Dictionary<string, Option<bool>> Flags, CancellationToken CancellationToken)
    {
        public string Get(string key) => Parse.GetValue(Options[key])!;
        public string? Optional(string key) => Parse.GetValue(Options[key]);
        public bool Flag(string key) => Parse.GetValue(Flags[key]);
        public int Number(string key, int defaultValue)
        {
            if (Optional(key) is not { } value) return defaultValue;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new RelayException("invalid_number", $"{key} 必须为正整数。");
            return number;
        }
        public int ExecutionTimeout(string key)
        {
            var seconds = Number(key, 180);
            if (seconds is < 1 or > 1800) throw new RelayException("invalid_execution_timeout", "执行时限必须为 1–1800 秒。");
            return seconds;
        }
        public DateTimeOffset? Timestamp(string key)
        {
            if (Optional(key) is not { } value) return null;
            if (!DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "O"], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var result))
                throw new RelayException("invalid_timestamp", $"{key} 必须为 UTC RFC3339 时间，例如 2026-10-22T10:30:00Z。");
            return result.ToUniversalTime();
        }
    }
}
