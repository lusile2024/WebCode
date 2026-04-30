namespace WebCodeCli.Domain.Domain.Model;

public static class SuperpowersQuickActionDefaults
{
    public const string QuickInputFieldName = "superpowers_quick_input";
    public const string QuickInputPlaceholder = "输入内容后回车，未写前缀时会自动补成使用superpowers技能，";
    public const string InstructionText = "可直接输入 superpowers 指令；未填写前缀时，会自动补成“使用superpowers技能，”。";

    public const string ExecutePlanButtonText = "执行 plan";
    public const string ExecuteSubagentPlanButtonText = "子代理执行 plan";

    public const string ExecutePlanPrompt = "使用superpowers的executing-plans技能执行计划";
    public const string ExecuteSubagentPlanPrompt = "使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能";
    public const string QuickSkillPrefix = "使用superpowers技能，";
}
