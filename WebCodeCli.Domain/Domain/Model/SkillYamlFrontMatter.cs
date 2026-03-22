namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 技能文档YAML Front Matter解析模型
/// </summary>
public class SkillYamlFrontMatter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public string[] Alias { get; set; } = Array.Empty<string>();
    public string Category { get; set; } = string.Empty;
}
