namespace TaskBaton.Core;

public sealed record RelaySkill(string Name, string Path);

/// <summary>
/// Skills Relay ships for the Agents that configure it. Each is a SKILL.md folder, written under the Relay home so a
/// setup prompt can point at it, and copyable into an Agent's own skills folder for hosts that load skills.
/// </summary>
public sealed partial class RelayService
{
    public static readonly string[] SkillNames = ["relay-cache-parity"];

    public RelaySkill Skill(string name)
    {
        var text = SkillText(name);
        var path = Path.Combine(Home, "skills", name, "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Rewritten on every use so the copy always matches this Relay version.
        if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        return new(name, path);
    }

    /// <summary>Copies a skill into an Agent's skills folder, e.g. ~/.claude/skills; replaces only that skill's own SKILL.md.</summary>
    public RelaySkill InstallSkill(string name, string skillsDirectory)
    {
        if (!Path.IsPathFullyQualified(skillsDirectory))
            throw new RelayException("skill_target", "请填写 Agent 技能目录的完整路径。");
        var path = Path.Combine(skillsDirectory, name, "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SkillText(name));
        return new(name, path);
    }

    private static string SkillText(string name)
    {
        if (!SkillNames.Contains(name, StringComparer.Ordinal))
            throw new RelayException("skill_not_found", "没有这个技能：" + name + "。可用：" + string.Join("、", SkillNames));
        using var stream = typeof(RelayService).Assembly.GetManifestResourceStream("TaskBaton.Skills." + name)
            ?? throw new InvalidOperationException("Skill resource missing: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
