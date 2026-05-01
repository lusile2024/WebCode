using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 椋炰功娓犻亾鏈嶅姟鎺ュ彛
/// </summary>
public interface IFeishuChannelService
{
    /// <summary>
    /// 鏈嶅姟鏄惁杩愯涓?
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 鍙戦€佹枃鏈秷鎭?
    /// </summary>
    /// <param name="chatId">浼氳瘽 ID</param>
    /// <param name="content">娑堟伅鍐呭</param>
    /// <returns>娑堟伅 ID</returns>
    Task<string> SendMessageAsync(string chatId, string content, string? username = null, string? appId = null);

    /// <summary>
    /// 鍥炲娑堟伅
    /// </summary>
    /// <param name="messageId">瑕佸洖澶嶇殑娑堟伅 ID</param>
    /// <param name="content">鍥炲鍐呭</param>
    /// <returns>鍥炲娑堟伅 ID</returns>
    Task<string> ReplyMessageAsync(string messageId, string content, string? username = null, string? appId = null);

    /// <summary>
    /// 鍙戦€佹祦寮忔秷鎭紙鏍稿績鏂规硶锛?
    /// </summary>
    /// <param name="chatId">浼氳瘽 ID</param>
    /// <param name="initialContent">鍒濆鍐呭</param>
    /// <param name="replyToMessageId">瑕佸洖澶嶇殑娑堟伅 ID锛堝彲閫夛級</param>
    /// <returns>娴佸紡鍙ユ焺</returns>
    Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null,
        string? username = null,
        string? appId = null);

    /// <summary>
    /// 澶勭悊鏀跺埌鐨勬秷鎭紙鐢?FeishuMessageHandler 璋冪敤锛?
    /// </summary>
    /// <param name="message">鏀跺埌鐨勬秷鎭?/param>
    Task HandleIncomingMessageAsync(FeishuIncomingMessage message);

    /// <summary>
    /// 鑾峰彇鑱婂ぉ鐨勫綋鍓嶆椿璺冧細璇滻D
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿紙鏍煎紡锛歠eishu:{AppId}:{ChatId}锛?/param>
    /// <returns>褰撳墠浼氳瘽ID锛屽鏋滀笉瀛樺湪鍒欒繑鍥瀗ull</returns>
    string? GetCurrentSession(string chatKey, string? username = null);

    /// <summary>
    /// 鑾峰彇浼氳瘽鐨勬渶鍚庢椿璺冩椂闂?
    /// </summary>
    /// <param name="sessionId">浼氳瘽ID</param>
    /// <returns>鏈€鍚庢椿璺冩椂闂达紝濡傛灉浼氳瘽涓嶅瓨鍦ㄥ垯杩斿洖null</returns>
    DateTime? GetSessionLastActiveTime(string sessionId);

    /// <summary>
    /// 鑾峰彇鑱婂ぉ鐨勬墍鏈変細璇滻D鍒楄〃
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿?/param>
    /// <returns>浼氳瘽ID鍒楄〃</returns>
    List<string> GetChatSessions(string chatKey, string? username = null);

    /// <summary>
    /// 鍒囨崲鑱婂ぉ鐨勫綋鍓嶆椿璺冧細璇?
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿?/param>
    /// <param name="sessionId">瑕佸垏鎹㈠埌鐨勪細璇滻D</param>
    /// <returns>鏄惁鍒囨崲鎴愬姛</returns>
    bool SwitchCurrentSession(string chatKey, string sessionId, string? username = null);

    /// <summary>
    /// 鍏抽棴鎸囧畾浼氳瘽
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿?/param>
    /// <param name="sessionId">瑕佸叧闂殑浼氳瘽ID</param>
    /// <returns>鏄惁鍏抽棴鎴愬姛</returns>
    bool CloseSession(string chatKey, string sessionId, string? username = null);

    /// <summary>
    /// 鍒ゆ柇浼氳瘽鏄惁姝ｅ湪鎵ц
    /// </summary>
    /// <param name="sessionId">浼氳瘽 ID</param>
    /// <returns>鏄惁鏈夋椿璺冩墽琛屼腑</returns>
    bool IsSessionExecutionActive(string sessionId);

    /// <summary>
    /// 暂停指定会话流式卡片的状态脉冲刷新一段时间
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="duration">暂停时长</param>
    void PauseSessionStatusPulse(string sessionId, TimeSpan duration);

    /// <summary>
    /// 鍒涘缓鏂颁細璇?
    /// </summary>
    /// <param name="message">椋炰功 incoming 娑堟伅</param>
    /// <param name="customWorkspacePath">鑷畾涔夊伐浣滃尯璺緞锛堝彲閫夛級</param>
    /// <param name="toolId">鎸囧畾宸ュ叿 ID锛堝彲閫夛級</param>
    /// <returns>鏂颁細璇滻D</returns>
    string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null, string? toolId = null);

    /// <summary>
    /// 鑾峰彇鑱婂ぉ缁戝畾浼氳瘽鐨勭敤鎴峰悕
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿?/param>
    /// <returns>鐢ㄦ埛鍚嶏紝濡傛灉涓嶅瓨鍦ㄥ垯杩斿洖null</returns>
    string? GetSessionUsername(string chatKey);

    /// <summary>
    /// 瑙ｆ瀽褰撳墠椋炰功鑱婂ぉ搴斾娇鐢ㄧ殑 CLI 宸ュ叿 ID
    /// 浼樺厛浣跨敤娲昏穬浼氳瘽缁戝畾鐨勫伐鍏凤紝鍏舵鍥為€€鍒伴涔﹂粯璁ゅ伐鍏峰拰棣栦釜鍙敤宸ュ叿
    /// </summary>
    /// <param name="chatKey">鑱婂ぉ閿?/param>
    /// <param name="username">鐢ㄦ埛鍚嶏紙鍙€夛級</param>
    /// <returns>宸ュ叿 ID</returns>
    string ResolveToolId(string chatKey, string? username = null);
}
