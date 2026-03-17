using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 椋炰功 CardKit 瀹㈡埛绔疄鐜?
/// </summary>
[ServiceDescription(typeof(IFeishuCardKitClient), ServiceLifetime.Scoped)]
public class FeishuCardKitClient : IFeishuCardKitClient
{
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuCardKitClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://open.feishu.cn";

    // Token 缂撳瓨
    private string _accessToken = string.Empty;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1); // 寮傛閿侊紝淇骞跺彂瀹夊叏闂
    private string? _lastValidToken; // 涓婃鏈夋晥鐨?token锛岀敤浜庡け璐ュ洖閫€

    public FeishuCardKitClient(
        IOptions<FeishuOptions> options,
        ILogger<FeishuCardKitClient> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("FeishuClient");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds > 0 ? _options.HttpTimeoutSeconds : 30);
    }

    public async Task<string> CreateCardAsync(
        string initialContent,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);

        var cardData = new
        {
            schema = "2.0",
            config = new
            {
                update_multi = true,
                streaming_mode = true
            },
            header = new
            {
                title = new
                {
                    tag = "plain_text",
                    content = title ?? _options.DefaultCardTitle
                }
            },
            body = new
            {
                elements = new[]
                {
                    new
                    {
                        tag = "markdown",
                        content = initialContent
                    }
                }
            }
        };

        var payload = new
        {
            type = "card_json",
            data = JsonSerializer.Serialize(cardData)
        };

        var response = await PostAsync("/open-apis/cardkit/v1/cards", token, payload, cancellationToken);
        var result = await ParseResponseAsync(response, cancellationToken);

        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("card_id", out var cardIdProp))
        {
            return cardIdProp.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Failed to create card: invalid response");
    }

    public async Task<bool> UpdateCardAsync(
        string cardId,
        string content,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await EnsureTokenAsync(cancellationToken);

            var cardData = new
            {
                schema = "2.0",
                config = new
                {
                    update_multi = true,
                    streaming_mode = true
                },
                body = new
                {
                    elements = new[]
                    {
                        new
                        {
                            tag = "markdown",
                            content = content
                        }
                    }
                }
            };

            var payload = new
            {
                card = new
                {
                    type = "card_json",
                    data = JsonSerializer.Serialize(cardData)
                },
                sequence
            };

            var response = await PutAsync($"/open-apis/cardkit/v1/cards/{cardId}", token, payload, cancellationToken);
            var result = await ParseResponseAsync(response, cancellationToken);

            if (result.TryGetProperty("code", out var codeProp))
            {
                var code = codeProp.GetInt32();
                if (code == 0) return true;

                _logger.LogWarning(
                    "Update card failed (cardId={CardId}, seq={Sequence}): Code={Code}, Msg={Msg}",
                    cardId, sequence, code,
                    result.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "Unknown");
                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update card failed (cardId={CardId}, seq={Sequence})", cardId, sequence);
            return false;
        }
    }

    public async Task<string> SendCardMessageAsync(
        string chatId,
        string cardId,
        CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(cancellationToken);

        var payload = new
        {
            receive_id = chatId,
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new
                {
                    card_id = cardId
                }
            })
        };

        var response = await PostAsync(
            "/open-apis/im/v1/messages?receive_id_type=chat_id",
            token,
            payload,
            cancellationToken);

        var result = await ParseResponseAsync(response, cancellationToken);

        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            return messageIdProp.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Failed to send card message: invalid response");
    }

    public async Task<string> ReplyCardMessageAsync(
        string replyMessageId,
        string cardId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("馃摛 [FeishuCardKit] ReplyCardMessageAsync: ReplyMessageId={ReplyMessageId}, CardId={CardId}",
            replyMessageId, cardId);

        var token = await EnsureTokenAsync(cancellationToken);

        var payload = new
        {
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new
                {
                    card_id = cardId
                }
            })
        };

        _logger.LogInformation("馃摛 [FeishuCardKit] 鍙戦€?POST 璇锋眰鍒?/open-apis/im/v1/messages/{ReplyMessageId}/reply", replyMessageId);
        var response = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            payload,
            cancellationToken);

        _logger.LogInformation("馃摛 [FeishuCardKit] 鍝嶅簲鐘舵€佺爜: {StatusCode}", response.StatusCode);
        var result = await ParseResponseAsync(response, cancellationToken);
        _logger.LogDebug("馃摛 [FeishuCardKit] 鍝嶅簲鍐呭: {Response}", result);

        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            var messageId = messageIdProp.GetString() ?? string.Empty;
            _logger.LogInformation("鉁?[FeishuCardKit] 鍥炲鎴愬姛, MessageId={MessageId}", messageId);
            return messageId;
        }

        _logger.LogError("鉂?[FeishuCardKit] 鍝嶅簲涓病鏈?message_id");
        throw new InvalidOperationException("Failed to reply card message: invalid response");
    }

    public async Task<FeishuStreamingHandle> CreateStreamingHandleAsync(
        string chatId,
        string? replyMessageId,
        string initialContent,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 鍒涘缓鍗＄墖
        var cardId = await CreateCardAsync(initialContent, title, cancellationToken);

        // 2. 鍙戦€佹垨鍥炲鍗＄墖娑堟伅
        string messageId;
        if (!string.IsNullOrEmpty(replyMessageId))
        {
            messageId = await ReplyCardMessageAsync(replyMessageId, cardId, cancellationToken);
        }
        else
        {
            messageId = await SendCardMessageAsync(chatId, cardId, cancellationToken);
        }

        // 3. 鍒涘缓娴佸紡鍙ユ焺
        return new FeishuStreamingHandle(
            cardId,
            messageId,
            content => UpdateCardAsync(cardId, content, Sequence, cancellationToken),
            content => UpdateCardAsync(cardId, content, Sequence + 1, cancellationToken),
            _options.StreamingThrottleMs
        );
    }

    private int _sequence = 0;
    private int Sequence => Interlocked.Increment(ref _sequence);

    /// <summary>
    /// 鑾峰彇鎴栧埛鏂拌闂护鐗?
    /// 浣跨敤 SemaphoreSlim 瀹炵幇寮傛瀹夊叏鐨勫弻閲嶆鏌ラ攣瀹?
    /// </summary>
    private async Task<string> EnsureTokenAsync(CancellationToken cancellationToken)
    {
        // 蹇€熻矾寰勶細token 鏈夋晥鐩存帴杩斿洖
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // 鍙岄噸妫€鏌?
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var payload = new
            {
                app_id = _options.AppId,
                app_secret = _options.AppSecret
            };

            HttpResponseMessage response;
            try
            {
                response = await PostAsync(
                    "/open-apis/auth/v3/tenant_access_token/internal",
                    string.Empty,
                    payload,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh access token");
                // 鍥為€€锛氫娇鐢ㄤ笂娆℃湁鏁堢殑 token锛堝鏋滆繕鏈夋晥锛?
                if (!string.IsNullOrEmpty(_lastValidToken))
                {
                    _logger.LogWarning("Using fallback token due to refresh failure");
                    return _lastValidToken;
                }
                throw;
            }

            var result = await ParseResponseAsync(response, cancellationToken);

            if (result.TryGetProperty("tenant_access_token", out var tokenProp) &&
                result.TryGetProperty("expire", out var expireProp))
            {
                var newToken = tokenProp.GetString() ?? string.Empty;
                var expireSeconds = expireProp.GetInt32();

                _accessToken = newToken;
                _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expireSeconds - 60);
                _lastValidToken = newToken; // 淇濆瓨鏈夋晥 token 鐢ㄤ簬鍥為€€

                _logger.LogDebug("Access token refreshed, expires at {ExpiresAt}", _tokenExpiresAt);
                return _accessToken;
            }

            // 瑙ｆ瀽澶辫触浣嗗彲鑳借繕鏈夋棫 token 鍙敤
            if (!string.IsNullOrEmpty(_lastValidToken))
            {
                _logger.LogWarning("Token parse failed, using fallback token");
                return _lastValidToken;
            }

            throw new InvalidOperationException("Failed to get access token: invalid response");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        string path,
        string token,
        object payload,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{path}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> PutAsync(
        string path,
        string token,
        object payload,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{_baseUrl}{path}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string operationName = "API request")
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "API request failed: Status={Status}, Content={Content}",
                response.StatusCode,
                content);
            throw new HttpRequestException($"API request failed: {response.StatusCode}");
        }

        return EnsureBusinessSuccess(JsonDocument.Parse(content).RootElement, operationName);
    }

    private JsonElement EnsureBusinessSuccess(JsonElement result, string operationName)
    {
        if (!result.TryGetProperty("code", out var codeProp) || !TryGetBusinessCode(codeProp, out var code) || code == 0)
        {
            return result;
        }

        var message = result.TryGetProperty("msg", out var msgProp)
            ? msgProp.GetString() ?? "Unknown error"
            : "Unknown error";

        _logger.LogError(
            "{Operation} failed: Code={Code}, Msg={Msg}, Response={Response}",
            operationName,
            code,
            message,
            result);

        throw new InvalidOperationException($"{operationName} failed: {message} (code: {code})");
    }

    private static bool TryGetBusinessCode(JsonElement codeProp, out int code)
    {
        if (codeProp.ValueKind == JsonValueKind.Number)
        {
            return codeProp.TryGetInt32(out code);
        }

        if (codeProp.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(codeProp.GetString(), out code);
        }

        code = default;
        return false;
    }
    /// <summary>
    /// 发送原始 JSON 卡片消息（帮助功能专用）
    /// 使用消息 API 直接发送，避免 card_id 引用在客户端回退成占位文本。
    /// </summary>
    public async Task<string> SendRawCardAsync(
        string chatId,
        string cardJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FeishuCardKit] Create CardKit card then send message, ChatId={ChatId}", chatId);
        _logger.LogDebug("[FeishuCardKit] Card JSON: {CardJson}", cardJson);

        var token = await EnsureTokenAsync(cancellationToken);
        var createPayload = new
        {
            type = "card_json",
            data = cardJson
        };

        var createResponse = await PostAsync(
            "/open-apis/cardkit/v1/cards",
            token,
            createPayload,
            cancellationToken);

        var createResult = await ParseResponseAsync(createResponse, cancellationToken, "Create CardKit card");
        var cardId = ExtractCardId(createResult, "Create CardKit card");

        var sendPayload = new
        {
            receive_id = chatId,
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new
                {
                    card_id = cardId
                }
            })
        };

        var sendResponse = await PostAsync(
            "/open-apis/im/v1/messages?receive_id_type=chat_id",
            token,
            sendPayload,
            cancellationToken);

        var sendResult = await ParseResponseAsync(sendResponse, cancellationToken, "Send interactive card message");
        return ExtractMessageId(sendResult, "Send interactive card message");
    }

    /// <summary>
    /// 回复 V2 DTO 卡片消息（帮助功能专用）
    /// </summary>
    public Task<string> ReplyElementsCardAsync(
        string replyMessageId,
        ElementsCardV2Dto card,
        CancellationToken cancellationToken = default)
    {
        var cardJson = SerializeElementsCard(card);
        return ReplyRawCardAsync(replyMessageId, cardJson, cancellationToken);
    }

    /// <summary>
    /// 回复原始 JSON 卡片消息（帮助功能专用）
    /// 使用消息 API 直接回复交互卡片，并带上 uuid 做幂等去重。
    /// </summary>
    public async Task<string> ReplyRawCardAsync(
        string replyMessageId,
        string cardJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FeishuCardKit] Create CardKit card then reply, ReplyMessageId={ReplyMessageId}", replyMessageId);
        _logger.LogDebug("[FeishuCardKit] Card JSON: {CardJson}", cardJson);

        var token = await EnsureTokenAsync(cancellationToken);
        var createPayload = new
        {
            type = "card_json",
            data = cardJson
        };

        var createResponse = await PostAsync(
            "/open-apis/cardkit/v1/cards",
            token,
            createPayload,
            cancellationToken);

        var createResult = await ParseResponseAsync(createResponse, cancellationToken, "Create CardKit card");
        var cardId = ExtractCardId(createResult, "Create CardKit card");

        var replyPayload = new
        {
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new
                {
                    card_id = cardId
                }
            })
        };

        var replyResponse = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            replyPayload,
            cancellationToken);

        var replyResult = await ParseResponseAsync(replyResponse, cancellationToken, "Reply interactive card message");
        return ExtractMessageId(replyResult, "Reply interactive card message");
    }

    private static string ExtractMessageId(JsonElement result, string operationName)
    {
        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            return messageIdProp.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException($"{operationName} failed: missing message_id");
    }

    private static string ExtractCardId(JsonElement result, string operationName)
    {
        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("card_id", out var cardIdProp))
        {
            return cardIdProp.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException($"{operationName} failed: missing card_id");
    }

    private static string SerializeElementsCard(ElementsCardV2Dto card)
    {
        var payload = new
        {
            schema = string.IsNullOrWhiteSpace(card.Schema) ? "2.0" : card.Schema,
            config = card.Config,
            header = card.Header,
            card_link = card.CardLink,
            body = card.Body
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}


