using System.Text.Json;
using System.Text.Json.Serialization;
using FeishuNetSdk.Im.Dtos;

namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 原始JSON卡片包装类
/// 用于将现有的JSON卡片字符串包装为SDK的MessageCard类型
/// </summary>
public record RawJsonCard : MessageCard
{
    /// <summary>
    /// 卡片数据（JSON对象）
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; set; } = new();

    /// <summary>
    /// 从JSON字符串创建RawJsonCard
    /// </summary>
    public static RawJsonCard FromJson(string json)
    {
        var card = new RawJsonCard();
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (data != null)
        {
            card.Data = data;
        }
        return card;
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RawJsonCard() : base("interactive")
    {
    }
}
