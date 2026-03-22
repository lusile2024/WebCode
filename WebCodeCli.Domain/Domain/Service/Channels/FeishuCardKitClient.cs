using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书 CardKit 客户端实现
/// </summary>
[ServiceDescription(typeof(IFeishuCardKitClient), ServiceLifetime.Scoped)]
public class FeishuCardKitClient : IFeishuCardKitClient
{
    private readonly FeishuOptions _defaultOptions;
    private readonly ILogger<FeishuCardKitClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://open.feishu.cn";
    private readonly ConcurrentDictionary<string, TokenCacheEntry> _tokenCache = new(StringComparer.Ordinal);

    public FeishuCardKitClient(
        IOptions<FeishuOptions> options,
        ILogger<FeishuCardKitClient> logger,
        IHttpClientFactory httpClientFactory)
    {
        _defaultOptions = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("FeishuClient");
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<string> CreateCardAsync(
        string initialContent,
        string? title = null,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

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
                    content = title ?? effectiveOptions.DefaultCardTitle
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

        var response = await PostAsync("/open-apis/cardkit/v1/cards", token, payload, effectiveOptions, cancellationToken);
        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Create CardKit card");

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
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        try
        {
            var effectiveOptions = GetEffectiveOptions(optionsOverride);
            var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

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

            var response = await PutAsync($"/open-apis/cardkit/v1/cards/{cardId}", token, payload, effectiveOptions, cancellationToken);
            var result = await ParseResponseAsync(response, cancellationToken);
            EnsureBusinessSuccess(result, "Update CardKit card");

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
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

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
            effectiveOptions,
            cancellationToken);

        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Send Feishu card message");
        return ExtractMessageId(result, "send card message");
    }

    public async Task<string> SendTextMessageAsync(
        string chatId,
        string content,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

        var payload = new
        {
            receive_id = chatId,
            msg_type = "text",
            content = JsonSerializer.Serialize(new
            {
                text = content
            })
        };

        var response = await PostAsync(
            "/open-apis/im/v1/messages?receive_id_type=chat_id",
            token,
            payload,
            effectiveOptions,
            cancellationToken);

        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Send Feishu text message");
        return ExtractMessageId(result, "send text message");
    }

    public async Task<string> ReplyCardMessageAsync(
        string replyMessageId,
        string cardId,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        _logger.LogInformation("📤 [FeishuCardKit] ReplyCardMessageAsync: ReplyMessageId={ReplyMessageId}, CardId={CardId}",
            replyMessageId, cardId);

        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

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

        _logger.LogInformation("📤 [FeishuCardKit] 发送 POST 请求到 /open-apis/im/v1/messages/{ReplyMessageId}/reply", replyMessageId);
        var response = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            payload,
            effectiveOptions,
            cancellationToken);

        _logger.LogInformation("📤 [FeishuCardKit] 响应状态码: {StatusCode}", response.StatusCode);
        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Reply Feishu card message");
        _logger.LogDebug("📤 [FeishuCardKit] 响应内容: {Response}", result);
        var messageId = ExtractMessageId(result, "reply card message");
        _logger.LogInformation("✅ [FeishuCardKit] 回复成功, MessageId={MessageId}", messageId);
        return messageId;
    }

    public async Task<string> ReplyTextMessageAsync(
        string replyMessageId,
        string content,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

        var payload = new
        {
            msg_type = "text",
            content = JsonSerializer.Serialize(new
            {
                text = content
            })
        };

        var response = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            payload,
            effectiveOptions,
            cancellationToken);

        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Reply Feishu text message");
        return ExtractMessageId(result, "reply text message");
    }

    public async Task<FeishuStreamingHandle> CreateStreamingHandleAsync(
        string chatId,
        string? replyMessageId,
        string initialContent,
        string? title = null,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);

        // 1. 创建卡片
        var cardId = await CreateCardAsync(initialContent, title, cancellationToken, effectiveOptions);

        // 2. 发送或回复卡片消息
        string messageId;
        if (!string.IsNullOrEmpty(replyMessageId))
        {
            messageId = await ReplyCardMessageAsync(replyMessageId, cardId, cancellationToken, effectiveOptions);
        }
        else
        {
            messageId = await SendCardMessageAsync(chatId, cardId, cancellationToken, effectiveOptions);
        }

        // 3. 创建流式句柄
        return new FeishuStreamingHandle(
            cardId,
            messageId,
            content => UpdateCardAsync(cardId, content, Sequence, cancellationToken, effectiveOptions),
            content => UpdateCardAsync(cardId, content, Sequence + 1, cancellationToken, effectiveOptions),
            effectiveOptions.StreamingThrottleMs
        );
    }

    private int _sequence = 0;
    private int Sequence => Interlocked.Increment(ref _sequence);

    private string ExtractMessageId(JsonElement result, string operationName)
    {
        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            return messageIdProp.GetString() ?? string.Empty;
        }

        _logger.LogError("❌ [FeishuCardKit] 响应中没有 message_id, Operation={Operation}", operationName);
        throw new InvalidOperationException($"Failed to {operationName}: invalid response");
    }

    /// <summary>
    /// 获取或刷新访问令牌
    /// 使用 SemaphoreSlim 实现异步安全的双重检查锁定
    /// </summary>
    private async Task<string> EnsureTokenAsync(FeishuOptions options, CancellationToken cancellationToken)
    {
        var cacheEntry = GetTokenCacheEntry(options);

        // 快速路径：token 有效直接返回
        if (!string.IsNullOrEmpty(cacheEntry.AccessToken) && DateTime.UtcNow < cacheEntry.TokenExpiresAt)
        {
            return cacheEntry.AccessToken;
        }

        await cacheEntry.TokenLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查
            if (!string.IsNullOrEmpty(cacheEntry.AccessToken) && DateTime.UtcNow < cacheEntry.TokenExpiresAt)
            {
                return cacheEntry.AccessToken;
            }

            var payload = new
            {
                app_id = options.AppId,
                app_secret = options.AppSecret
            };

            HttpResponseMessage response;
            try
            {
                response = await PostAsync(
                    "/open-apis/auth/v3/tenant_access_token/internal",
                    string.Empty,
                    payload,
                    options,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh access token for AppId={AppId}", options.AppId);
                // 回退：使用上次有效的 token（如果还有效）
                if (!string.IsNullOrEmpty(cacheEntry.LastValidToken))
                {
                    _logger.LogWarning("Using fallback token due to refresh failure for AppId={AppId}", options.AppId);
                    return cacheEntry.LastValidToken;
                }
                throw;
            }

            var result = await ParseResponseAsync(response, cancellationToken);
            EnsureBusinessSuccess(result, "Refresh Feishu tenant token");

            if (result.TryGetProperty("tenant_access_token", out var tokenProp) &&
                result.TryGetProperty("expire", out var expireProp))
            {
                var newToken = tokenProp.GetString() ?? string.Empty;
                var expireSeconds = expireProp.GetInt32();

                cacheEntry.AccessToken = newToken;
                cacheEntry.TokenExpiresAt = DateTime.UtcNow.AddSeconds(expireSeconds - 60);
                cacheEntry.LastValidToken = newToken;

                _logger.LogDebug("Access token refreshed for AppId={AppId}, expires at {ExpiresAt}", options.AppId, cacheEntry.TokenExpiresAt);
                return cacheEntry.AccessToken;
            }

            // 解析失败但可能还有旧 token 可用
            if (!string.IsNullOrEmpty(cacheEntry.LastValidToken))
            {
                _logger.LogWarning("Token parse failed, using fallback token for AppId={AppId}", options.AppId);
                return cacheEntry.LastValidToken;
            }

            throw new InvalidOperationException("Failed to get access token: invalid response");
        }
        finally
        {
            cacheEntry.TokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        string path,
        string token,
        object payload,
        FeishuOptions options,
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

        return await SendAsync(request, options, cancellationToken);
    }

    private async Task<HttpResponseMessage> PutAsync(
        string path,
        string token,
        object payload,
        FeishuOptions options,
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

        return await SendAsync(request, options, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        FeishuOptions options,
        CancellationToken cancellationToken)
    {
        if (options.HttpTimeoutSeconds <= 0)
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.HttpTimeoutSeconds));
        return await _httpClient.SendAsync(request, timeoutCts.Token);
    }

    private async Task<JsonElement> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
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

        return JsonDocument.Parse(content).RootElement;
    }

    private void EnsureBusinessSuccess(JsonElement result, string operationName)
    {
        if (!result.TryGetProperty("code", out var codeProp))
        {
            return;
        }

        var code = codeProp.GetInt32();
        if (code == 0)
        {
            return;
        }

        var message = result.TryGetProperty("msg", out var msgProp)
            ? msgProp.GetString()
            : "Unknown error";

        throw new InvalidOperationException($"{operationName} failed: {message} (code: {code})");
    }

    private FeishuOptions GetEffectiveOptions(FeishuOptions? optionsOverride)
    {
        return optionsOverride ?? _defaultOptions;
    }

    private TokenCacheEntry GetTokenCacheEntry(FeishuOptions options)
    {
        var cacheKey = $"{options.AppId}\n{options.AppSecret}";
        return _tokenCache.GetOrAdd(cacheKey, _ => new TokenCacheEntry());
    }

    /// <summary>
    /// 发送原始JSON卡片消息（帮助功能专用）
    /// 通过 CardKit 创建卡片，避免JSON格式问题
    /// </summary>
    public async Task<string> SendRawCardAsync(
        string chatId,
        string cardJson,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        _logger.LogInformation("📤 [FeishuCardKit] 通过CardKit发送卡片");

        // 1. 先用 CardKit API 创建卡片
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

        var createCardPayload = new
        {
            type = "card_json",
            data = cardJson
        };

        var createResponse = await PostAsync(
            "/open-apis/cardkit/v1/cards",
            token,
            createCardPayload,
            effectiveOptions,
            cancellationToken);

        var createResult = await ParseResponseAsync(createResponse, cancellationToken);
        EnsureBusinessSuccess(createResult, "Create raw CardKit card");

        if (!createResult.TryGetProperty("data", out var createData) ||
            !createData.TryGetProperty("card_id", out var cardIdProp))
        {
            throw new InvalidOperationException("Failed to create card via CardKit");
        }

        var cardId = cardIdProp.GetString() ?? string.Empty;
        _logger.LogInformation("📤 [FeishuCardKit] CardKit创建成功: CardId={CardId}", cardId);

        // 2. 再发送卡片消息
        return await SendCardMessageAsync(chatId, cardId, cancellationToken, effectiveOptions);
    }

    /// <summary>
    /// 回复 V2 DTO 卡片消息（帮助功能专用）
    /// </summary>
    public Task<string> ReplyElementsCardAsync(
        string replyMessageId,
        ElementsCardV2Dto card,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var cardJson = SerializeElementsCard(card);
        return ReplyRawCardAsync(replyMessageId, cardJson, cancellationToken, optionsOverride);
    }

    /// <summary>
    /// 回复原始JSON卡片消息(帮助功能专用)
    /// 参考 OpenCowork 实现:先创建卡片获取 card_id,再发送
    /// </summary>
    public async Task<string> ReplyRawCardAsync(
        string replyMessageId,
        string cardJson,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        _logger.LogInformation("📤 [FeishuCardKit] 回复交互式卡片, ReplyMessageId={ReplyMessageId}", replyMessageId);
        _logger.LogDebug("📤 [FeishuCardKit] 卡片JSON: {CardJson}", cardJson);

        try
        {
            var effectiveOptions = GetEffectiveOptions(optionsOverride);
            var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

            // 步骤1: 使用 CardKit API 创建卡片,获取 card_id
            _logger.LogInformation("📤 [FeishuCardKit] 步骤1: 创建卡片...");

            var createCardPayload = new
            {
                type = "card_json",
                data = cardJson  // cardJson 是字符串
            };

            var createResponse = await PostAsync(
                "/open-apis/cardkit/v1/cards",
                token,
                createCardPayload,
                effectiveOptions,
                cancellationToken);

            var createContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("📤 [FeishuCardKit] 创建卡片响应: {Response}", createContent);

            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogError("❌ [FeishuCardKit] 创建卡片失败: {Content}", createContent);
                throw new HttpRequestException($"Create card failed: {createResponse.StatusCode}");
            }

            var createResult = JsonDocument.Parse(createContent).RootElement;
            EnsureBusinessSuccess(createResult, "Create reply CardKit card");

            if (!createResult.TryGetProperty("data", out var createData) ||
                !createData.TryGetProperty("card_id", out var cardIdProp))
            {
                _logger.LogError("❌ [FeishuCardKit] 响应中没有 card_id");
                throw new InvalidOperationException("Failed to get card_id from response");
            }

            var cardId = cardIdProp.GetString() ?? string.Empty;
            _logger.LogInformation("📤 [FeishuCardKit] 步骤1: 卡片创建成功, CardId={CardId}", cardId);

            // 步骤2: 使用消息 API 回复卡片(发送 card_id)
            _logger.LogInformation("📤 [FeishuCardKit] 步骤2: 回复卡片消息...");

            var replyPayload = new
            {
                msg_type = "interactive",
                content = JsonSerializer.Serialize(new
                {
                    type = "card",
                    data = new { card_id = cardId }
                })
            };

            var replyResponse = await PostAsync(
                $"/open-apis/im/v1/messages/{replyMessageId}/reply",
                token,
                replyPayload,
                effectiveOptions,
                cancellationToken);

            var replyContent = await replyResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("📤 [FeishuCardKit] 回复消息响应: {Response}", replyContent);

            if (!replyResponse.IsSuccessStatusCode)
            {
                _logger.LogError("❌ [FeishuCardKit] 回复消息失败: {Content}", replyContent);
                throw new HttpRequestException($"Reply message failed: {replyResponse.StatusCode}");
            }

            var replyResult = JsonDocument.Parse(replyContent).RootElement;
            EnsureBusinessSuccess(replyResult, "Reply raw Feishu card message");

            if (replyResult.TryGetProperty("data", out var data) &&
                data.TryGetProperty("message_id", out var messageIdProp))
            {
                var messageId = messageIdProp.GetString() ?? string.Empty;
                _logger.LogInformation("✅ [FeishuCardKit] 卡片回复成功, MessageId={MessageId}", messageId);
                return messageId;
            }

            _logger.LogError("❌ [FeishuCardKit] 响应中没有 message_id");
            throw new InvalidOperationException("Failed to get message_id from response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuCardKit] ReplyRawCardAsync 失败");
            throw;
        }
    }

    private sealed class TokenCacheEntry
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime TokenExpiresAt { get; set; } = DateTime.MinValue;
        public string? LastValidToken { get; set; }
        public SemaphoreSlim TokenLock { get; } = new(1, 1);
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

        return JsonSerializer.Serialize(payload);
    }
}
