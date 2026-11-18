namespace TaskBaton.Core;

/// <param name="Blocking">Retrying unattended cannot succeed until the user changes the host or recipe.</param>
public sealed record CliFailure(string Kind, string Message, bool Blocking);

public static class CliFailureDiagnostics
{
    private static readonly string[] AuthenticationSignals =
        ["Authentication required", "Please use /login", "Not logged in", "Please log in", "login required"];

    private static readonly string[] QuotaSignals = ["Insufficient balance", "no resource package", "insufficient_quota", "quota exceeded"];

    public static string Explain(ProcessResult process) => Classify(process).Message;

    public static CliFailure Classify(ProcessResult process, CliRecipe? recipe = null)
    {
        // These are known external host diagnostics. Keep full output in the probe's diagnostic detail.
        var output = process.Error + "\n" + process.Output;
        if (recipe is not null && CliRecipes.OutputFormatFor(recipe) == "dsh-desktop" &&
            process.Error.StartsWith("DSH desktop prompt accepted; observation failed: ", StringComparison.Ordinal))
            return new("delivery_unconfirmed", "DSH 已接受本轮请求，但 Relay 未能确认执行结果。请检查目标会话和任务回执，不要重复发送。原始错误：" + process.Error, false);
        if (output.Contains("Select a model before", StringComparison.OrdinalIgnoreCase))
            return new("model_not_selected", "目标 CLI 尚未选择可用模型。请先在宿主选择默认模型，或在任务中选择已有且已配置模型的会话继续。Relay 不会猜测模型；原始错误见 CLI 诊断详情。", true);
        // Some hosts print the refusal and still exit 0, so the exit code cannot rule this out.
        if (AuthenticationSignals.Any(signal => output.Contains(signal, StringComparison.OrdinalIgnoreCase)) ||
            output.Contains("DSH 桌面拒绝了 Relay 的登录（401）", StringComparison.Ordinal))
        {
            if (recipe is not null && CliRecipes.OutputFormatFor(recipe) == "dsh-desktop")
            {
                // Our desktop client emits this controlled 401 message with listener metadata, never cookie or response content.
                var diagnostic=process.Error.Contains("DSH 桌面拒绝了 Relay 的登录（401）",StringComparison.Ordinal)
                    ? "\n"+(process.Error.Length>8192?process.Error[..8192]:process.Error):"";
                return new("auth_required", "DSH 桌面曾拒绝本机接口认证，自动发送已暂停。请先检查当前连接和桌面桥接方案；连接检查通过后可重新检测并解除阻塞。此方案不需要另行登录 headless CLI。"+diagnostic, true);
            }
            return new("auth_required", "目标 CLI 没有可用的登录。桌面应用的登录不一定能被命令行使用，需要为该 CLI 单独登录" +
                (string.IsNullOrWhiteSpace(recipe?.Login) ? "。" : "：在终端运行 " + recipe.Login + " 并按提示完成。") +
                "完成后在 Agent 管理中点“我已处理，重新检测”。原始错误见 CLI 诊断详情。", true);
        }
        if (QuotaSignals.Any(signal => output.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return new("quota_exhausted", "目标 CLI 所用的账号套餐余额不足或没有可用资源包，模型拒绝了请求。请在宿主里确认命令行使用的套餐（可能与桌面端不同）或充值后，在 Agent 管理中点“我已处理，重新检测”。原始错误见 CLI 诊断详情。", true);
        if (process.TimedOut)
            return new("timeout", "CLI 等待超时或已取消，可能已经提交。请检查目标会话，不要重复发送。", false);
        if (process.ExitCode != 0)
            return new("exit_error", "CLI 返回错误：" + process.Error, false);
        return new("output_unrecognized", "未能从配置的输出格式读取准确的 CLI 结果。请检查输出格式与诊断详情；续接必须返回同一会话 ID。", false);
    }

    public static CliFailure PermissionRequired(CliRecipe recipe) => new("permission_required",
        "CLI 回复了探针标识，但没有写出要求的文件：无人值守调用时缺少工具权限，Agent 无法写回执。" +
        "请在调用方案中加入该宿主文档列出的非交互授权参数" +
        (CliRecipes.OutputFormatFor(recipe) == "claude-json" ? "（CodeBuddy/WorkBuddy 为 -y 或 --permission-mode）" : "") + "后重新测试。", true);
}
