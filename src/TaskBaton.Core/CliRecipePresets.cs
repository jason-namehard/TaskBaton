namespace TaskBaton.Core;

/// <summary>
/// A reviewable starting point for a host's CLI recipe. A preset is never treated as a verified connection:
/// the user still reviews the command, imports it, and runs the doctor or a probe.
/// </summary>
public sealed record CliRecipePreset(string Id, string Name, string Host, string Description,
    string Executable, string[] Arguments, string? ResumeExecutable, string[]? ResumeArguments,
    string OutputFormat, string[] Requires, string Source, string License, string Notes);

public static class CliRecipePresetCatalog
{
    public static IReadOnlyList<CliRecipePreset> All { get; } =
    [
        new("codex-acp", "Codex (ACP bridge)", "codex",
            "Use the official Agent Client Protocol adapter for Codex. ACP session/new and session/load map to Codex App Server.",
            "codex-acp", [], null, null, "acp",
            ["codex-acp", "codex"], "https://github.com/agentclientprotocol/codex-acp", "Apache-2.0",
            "Preset only. Install @agentclientprotocol/codex-acp and Codex CLI first; Relay does not bundle or install them. The bridge is not verified until a probe or a real handoff succeeds."),
        new("claude-acp", "Claude Code (ACP bridge)", "claude",
            "Use the Agent Client Protocol adapter powered by the Claude Agent SDK; ACP handles new and resumed sessions.",
            "claude-agent-acp", [], null, null, "acp",
            ["claude-agent-acp", "claude"], "https://github.com/agentclientprotocol/claude-agent-acp", "Apache-2.0",
            "Preset only. Install @agentclientprotocol/claude-agent-acp and Claude Code first; Relay does not bundle them. This is the ACP alternative to the CLI --resume preset."),
        new("claude-code", "Claude Code (CLI resume)", "claude",
            "Use Claude Code's official print mode with stream-json and exact --resume.",
            "claude", ["-p", "{prompt}", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits"],
            "claude", ["--resume", "{session_id}", "-p", "{prompt}", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits"],
            "claude-json", ["claude"], "https://docs.anthropic.com/en/docs/claude-code/cli-reference", "Proprietary CLI; use your installed copy",
            "Preset only. --permission-mode acceptEdits is required for unattended receipt writes. stream-json carries the session id; verify it with a probe before trusting resume."),
        new("gemini-cli", "Gemini CLI (experimental)", "gemini",
            "Start with Gemini CLI print mode. Exact resume is not declared by this preset because it varies by version.",
            "gemini", ["-p", "{prompt}", "--output-format", "json"], null, null, "text",
            ["gemini"], "https://github.com/google-gemini/gemini-cli", "Apache-2.0",
            "Preset only and experimental. Verify --output-format json and any resume flag with the installed version; do not assume exact resume until a probe passes."),
        new("opencode", "OpenCode (experimental)", "opencode",
            "Start with OpenCode's non-interactive run command. Exact resume is not declared by this preset because it varies by version.",
            "opencode", ["run", "{prompt}"], null, null, "text",
            ["opencode"], "https://github.com/sst/opencode", "MIT",
            "Preset only and experimental. Review the installed CLI's help for session flags before relying on continuation.")
    ];

    public static CliRecipePreset? Find(string id) => All.SingleOrDefault(preset => preset.Id == id);
}
