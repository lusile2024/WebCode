using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.FeishuUserBinding;

/// <summary>
/// 飞书用户与 Web 用户绑定实体
/// </summary>
[SugarTable("FeishuUserBinding")]
[SugarIndex("idx_feishu_user_id", nameof(FeishuUserId), OrderByType.Asc, IsUnique = true)]
public class FeishuUserBindingEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 128, IsNullable = false)]
    public string FeishuUserId { get; set; } = string.Empty;

    [SugarColumn(Length = 128, IsNullable = false)]
    public string WebUsername { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
