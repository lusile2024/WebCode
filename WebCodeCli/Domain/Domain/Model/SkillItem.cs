namespace WebCodeCli.Domain.Domain.Model;

public class SkillItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "claude" / "codex" / "opencode"
}
