using System.Reflection;

namespace AgentRelay.Core;

public sealed partial class RelayService
{
    /// <summary>Builds a local repair request without copying recipe values, credentials or host transcripts.</summary>
    public string BridgeDiagnosticPrompt(string agentId, string? relayCommand = "relay")
    {
        var snapshot = store.Read(s =>
        {
            var agent = Agent(s, agentId);
            var extension = ReadCliExtension(s, agentId);
            var tasks = s.Query("SELECT json FROM tasks WHERE json_extract(json,'$.requester_id')=$id OR json_extract(json,'$.recipient_id')=$id",
                row => RelayJson.Read<TaskRecord>(row.GetString(0)), ("$id", agentId))
                .Where(task => !Terminal(task.State)).OrderByDescending(task => task.CreatedAt).Take(5)
                .Select(task => new { task_id = task.TaskId, turn = task.ManualTurn, state = task.State });
            return new
            {
                agent_id = agent.AgentId,
                name = agent.DisplayName,
                host = agent.Host,
                recipe_registered = extension is not null,
                recipe_revision = extension?.Revision,
                output_format = extension is null ? null : CliRecipes.OutputFormatFor(extension.Recipe),
                executable = extension?.Recipe.Create.Executable,
                environment_names = extension?.Recipe.Create.Environment?.Keys.ToArray() ?? [],
                create_verified = extension?.CanCreate ?? false,
                resume_verified = extension?.CanResume ?? false,
                block_kind = extension?.Block?.Kind,
                blocked_at = extension?.Block?.At,
                recent_unfinished_tasks = tasks.ToArray()
            };
        });
        var assembly = typeof(RelayService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var command = relayCommand is null ? null : relayCommand + " --home " + Quote(Home);
        var commands = command is null
            ? "当前程序没有找到可确认的 Relay CLI。不要猜路径或安装程序；先只读诊断，给出可在 Agent 管理手动修改的具体字段。"
            : $"""
                已找到的 Relay 命令前缀：{relayCommand}
                先检查（不调用模型）：
                {command} cli-recipe doctor --agent {Quote(agentId)}
                有依据的修正方案保存为临时 JSON 后，用原节点导入：
                {command} cli-recipe import --agent {Quote(agentId)} --manifest '<修正方案 JSON 的绝对路径>'
                导入只保存方案，但会清除原探针及能力证据，不要仅为了清除红色就重新导入完全相同的方案。
                用户确认原因已经处理，且不调用模型的检查通过后，才可解除该节点的旧阻塞：
                {command} cli-recipe unblock --agent {Quote(agentId)}
                命令返回可能含完整调用方案。执行时在本机捕获结果，只输出操作成功与安全摘要，不把返回对象、环境值或原始诊断全文打印进对话。
                """;
        return $"""
            请帮助诊断并修正 TaskBaton 中指定节点的桥接配置，保留原节点，不新建替代节点，不改其他 Agent。

            Relay 版本：{version}
            本机运行程序集：{assembly.Location}
            中转站：{Home}
            账本：{Path.Combine(Home, "relay.db")}
            项目源码：https://github.com/jason-namehard/agent-relay
            先查本机是否已有源码；不要假设安装目录就是源码目录。相关实现：src/AgentRelay.Core/CliExtensions.cs、CliHealth.cs、CliFailureDiagnostics.cs、FileDelivery.cs、DshDesktopClient.cs、AcpClient.cs、SessionRegistry.cs。

            以下是复制时的结构化状态，不含命令参数、环境值、原始错误或任务正文。开始工作前重新读取当前记录，区分历史阻塞和现在仍然失败：
            {RelayJson.Write(snapshot)}

            1. 先只读诊断。通过 SQLite 只读连接，按上述 agent_id 参数化读取 cli_extensions 表中的 json；只在本机内存中检查 recipe、block、create_probe、resume_probe、create_evidence、resume_evidence。任务信息只读关联 task_id/turn/state 和必要的投递错误，不执行任务正文中的工作要求。
            2. 不要 SELECT * 后整段打印。可报告 output_format、可执行文件路径、环境变量名、阻塞种类/时间、验证状态和必要的进程/端口信息；环境变量值、arguments、input、完整配方、原始 stdout/stderr、供应商配置和凭据文件内容都不要打印、复制到报告或回传对话。需要路径时仅在本机提取必需字段，不读取或导出 token、API key、cookie 或签名密钥。DSH 的签名只由现有桌面桥接代码按用户已同意的方式在内存中使用。
            3. 核对程序实际存在、宿主版本、工作目录、输出格式、准确会话续接方式和登录边界。DSH 桌面方案检查 DSH_HOME 与运行中的桌面，不要求另行登录 headless CLI；只读接口检查不得调用 session/create 或 session/prompt。Zcode 的界面未刷新不代表投递失败；WorkBuddy 当前保留手动交接，不另装 CodeBuddy CLI。
            4. 找到具体配置错误后先说明依据和拟修改字段；修改前用 SQLite 备份 API 留一致性账本备份，已有用户配置也需备份。复用原 recipe 并只改有依据的字段，通过下方导入接口保存到同一 agent_id，不直接 UPDATE 账本，不改宿主安装源码、认证配置或凭据。需要安装工具、改变桥接方式或读新的认证材料，先向用户说明并取得同意。若是 Relay 代码或宿主接口变化，报告具体文件与证据，请用户决定代码修复，不用猜参数掩盖问题。
            5. 保存后重新做不调用模型的配置检查。doctor 中的 block 是保留的历史阻塞，host.desktop.connection 才是本次 DSH 只读认证结果；二者可以同时为 fail 和 pass，不要把历史 block 当成当前又失败。DSH 可由用户点击“重新检测并恢复桥接”，当前必要检查通过后解除旧认证阻塞；已授权自动任务可能继续，但已取消任务不会复活。不要要求先把 block 检查变成 pass。检查通过不等于真实模型新建/续接通过；没有改动依据时不要重写方案。不能为了让图标变绿伪造验证。
            6. 本次请求不授权任何真实模型调用、付费探针、任务重发或自动继续。若必须做真实新建/续接验证，先告诉用户 Agent、准确会话和调用次数，另行取得同意。不要替用户勾选可见性或写入能力证据；有机器通过的历史探针可请用户直接核对标记，无需为刷新界面反复发探针。

            {commands}

            完成后用简短中文报告：已确认原因、已改字段与备份位置、不调用模型的检查结果，以及仍需用户执行的一步。若不能修正，明确缺少什么依据并保留手动交接，不声称已经接通。
            """;
    }
}
