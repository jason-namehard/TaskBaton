using System.Globalization;

namespace AgentRelay.Core;

/// <summary>What a relay step is for. The role is declared by the user, never guessed from the Agent's prose.</summary>
public enum RelayStepRole { Work, Test, Modify }

/// <summary>One step of a lightweight fixed-count relay: who works, why, and what that step must do.</summary>
public sealed record RelayStep(string AgentId, RelayStepRole Role, string? Duty = null);

/// <summary>
/// A user-defined relay. Steps are turns: A→B is 2 steps and 1 handoff, A→B→A is 3 steps and 2 handoffs.
/// The last step is only the end of the flow; it is not an automatic acceptance or delivery role.
/// </summary>
public sealed record RelayPlanOptions(RelayStep[] Steps, bool IndependentAcceptance = false,
    int? TurnTimeoutMinutes = null, bool AutoDeliver = false, bool NewSessionEachTurn = false);

public sealed partial class RelayService
{
    public const int MaxRelaySteps = 9;

    /// <summary>Handoffs = steps - 1. "One test-and-modify round" is two handoffs, not one.</summary>
    public static int HandoffCount(int steps) => Math.Max(0, steps - 1);

    public static void ValidateRelayPlan(RelayPlanOptions options, string requesterId, string recipientId)
    {
        var steps = options.Steps ?? [];
        if (steps.Length is < 1 or > MaxRelaySteps)
            throw new RelayException("relay_steps", $"接力步骤为 1–{MaxRelaySteps} 步。");
        foreach (var step in steps)
        {
            if (step is null || step.AgentId != requesterId && step.AgentId != recipientId)
                throw new RelayException("relay_step_agent", "每一步都必须由本任务的发送方或接收方执行。");
        }
        for (var index = 1; index < steps.Length; index++)
            if (steps[index].AgentId == steps[index - 1].AgentId)
                throw new RelayException("relay_step_repeat", "相邻两步不能是同一个 Agent；相同 Agent 的连续工作请合并为一步。");
        if (steps[0].AgentId != recipientId && steps.Length == 1)
            throw new RelayException("relay_step_agent", "单步接力没有交接，请改用单次交接。");
        if (options.TurnTimeoutMinutes is < 1 or > 43200)
            throw new RelayException("handoff_turn_timeout", "单轮时限为 1–43200 分钟。");
    }

    /// <summary>The user sets the plan before the first delivery; it is not inferred from the ledger.</summary>
    public TaskView ConfigureRelayPlan(string taskId, RelayPlanOptions options) => store.Write(s =>
        ConfigureRelayPlan(s, s.Task(taskId), options));

    private TaskView ConfigureRelayPlan(StoreSession s, TaskRecord task, RelayPlanOptions options)
    {
        if (!task.IsFileHandoff || task.State != TaskState.AwaitingManual || task.ManualTurn != 0 ||
            task.ManualReceiptJson is not null || task.CliFileTurn is not null || task.RelaySteps is not null ||
            task.HandoffPlan is not null || task.Collaboration is not null)
            throw new RelayException("handoff_options_started", "接力步骤只能在首次投递和回执之前设置，且不能与验收循环同时使用。");
        ValidateRelayPlan(options, task.RequesterId, task.RecipientId);
        task.RelaySteps = options.Steps;
        task.IndependentAcceptance = options.IndependentAcceptance;
        task.NewSessionEachTurn = options.NewSessionEachTurn;
        task.TurnTimeoutMinutes = options.TurnTimeoutMinutes;
        task.AutoDeliver = options.AutoDeliver;
        task.ManualContinuous = false;
        // The first duty supplements the operator's original request, which may be separate from the handoff file.
        var first = options.Steps[0];
        task.AwaitingAgentId = first.AgentId;
        if (!string.IsNullOrWhiteSpace(first.Duty))
            task.ManualNextAction = string.IsNullOrWhiteSpace(task.ManualNextAction)
                ? first.Duty.Trim() : task.ManualNextAction + "\n\n" + first.Duty.Trim();
        task.DeadlineAt = Now + TurnTimeout(s, task);
        s.Save(task);
        s.Event(task, "handoff.relay_configured", "operator", Now, RelayJson.Write(options));
        return ManualView(task);
    }

    /// <summary>Steps as a plain list; legacy fixed plans have no duties or roles.</summary>
    internal static RelayStep[] Steps(TaskRecord task) =>
        task.RelaySteps ?? task.HandoffPlan?.Select(id => new RelayStep(id, RelayStepRole.Work)).ToArray() ?? [];

    internal static int HandoffsDone(TaskRecord task) => Math.Max(0, task.ManualTurn);

    internal static int? HandoffsTotal(TaskRecord task)
    {
        // Only a relay plan the user defined has a fixed handoff count; legacy plans keep their round budget.
        if (task.RelaySteps is { Length: > 0 } steps) return HandoffCount(steps.Length);
        return task.Collaboration is null && !task.ManualContinuous ? task.MaxTurns ?? MaxManualTurns : null;
    }

    /// <summary>
    /// A modification step that follows a test step and is the end of the flow was never re-tested.
    /// The label is only produced from the user's declared roles, so ordinary tasks never show it.
    /// </summary>
    internal static string? CompletionNote(TaskRecord task)
    {
        var steps = Steps(task);
        if (steps.Length == 0) return null;
        var last = steps[^1];
        if (last.Role != RelayStepRole.Modify) return null;
        return steps.Take(steps.Length - 1).Any(step => step.Role == RelayStepRole.Test) ? "modified_not_retested" : null;
    }

    internal static string StepDuty(TaskRecord task, int turn) =>
        turn >= 0 && turn < Steps(task).Length ? Steps(task)[turn].Duty?.Trim() ?? "" : "";

    /// <summary>Which Agent works on a given step, or null when the task has no plan.</summary>
    internal static string? StepAgent(TaskRecord task, int turn) =>
        turn >= 0 && turn < Steps(task).Length ? Steps(task)[turn].AgentId : null;

    /// <summary>Human-readable flow, e.g. "A → B → A (2 handoffs)".</summary>
    public string RelaySummary(TaskRecord task)
    {
        var steps = Steps(task);
        if (steps.Length == 0) return "";
        string Name(string id) => Agents().SingleOrDefault(a => a.AgentId == id)?.DisplayName ?? id;
        return string.Join(" → ", steps.Select(step => Name(step.AgentId))) +
            string.Create(CultureInfo.InvariantCulture, $" ({HandoffCount(steps.Length)} handoffs)");
    }

    /// <summary>
    /// Reads a plan written by the user or a script: steps separated by ';' or newlines, each
    /// "agent[:work|test|modify][=duty]". Roles are explicit, so a "modified without retest" result is never guessed.
    /// </summary>
    public static RelayStep[] ParseSteps(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) throw new RelayException("relay_steps", "请至少给出一步，例如 agent-b=实现;agent-a:test=测试。");
        var steps = new List<RelayStep>();
        foreach (var raw in spec.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dutyAt = raw.IndexOf('=');
            var head = dutyAt < 0 ? raw : raw[..dutyAt];
            var duty = dutyAt < 0 ? null : raw[(dutyAt + 1)..].Trim();
            var roleAt = head.IndexOf(':');
            var agent = (roleAt < 0 ? head : head[..roleAt]).Trim();
            var roleText = roleAt < 0 ? "" : head[(roleAt + 1)..].Trim();
            var role = roleText switch
            {
                "" or "work" => RelayStepRole.Work,
                "test" => RelayStepRole.Test,
                "modify" => RelayStepRole.Modify,
                var other => throw new RelayException("relay_step_role", $"未知步骤角色 {other}；可用 work、test、modify。")
            };
            if (string.IsNullOrWhiteSpace(agent)) throw new RelayException("relay_step_agent", "每一步都需要 Agent ID：" + raw);
            steps.Add(new RelayStep(agent, role, string.IsNullOrWhiteSpace(duty) ? null : duty));
        }
        return [.. steps];
    }

    /// <summary>The current step's declared duty, used in the prompt instead of a generic instruction.</summary>
    internal string CurrentDuty(TaskRecord task) => StepDuty(task, task.ManualTurn);
}
