using System.Text;
using System.Text.Json;
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
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuCardKitClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://open.feishu.cn";

    // Token 缓存
    private string _accessToken = string.Empty;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1); // 异步锁，修复并发安全问题
    private string? _lastValidToken; // 上次有效的 token，用于失败回退

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
        _logger.LogInformation("📤 [FeishuCardKit] ReplyCardMessageAsync: ReplyMessageId={ReplyMessageId}, CardId={CardId}",
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

        _logger.LogInformation("📤 [FeishuCardKit] 发送 POST 请求到 /open-apis/im/v1/messages/{ReplyMessageId}/reply", replyMessageId);
        var response = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            payload,
            cancellationToken);

        _logger.LogInformation("📤 [FeishuCardKit] 响应状态码: {StatusCode}", response.StatusCode);
        var result = await ParseResponseAsync(response, cancellationToken);
        _logger.LogDebug("📤 [FeishuCardKit] 响应内容: {Response}", result);

        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            var messageId = messageIdProp.GetString() ?? string.Empty;
            _logger.LogInformation("✅ [FeishuCardKit] 回复成功, MessageId={MessageId}", messageId);
            return messageId;
        }

        _logger.LogError("❌ [FeishuCardKit] 响应中没有 message_id");
        throw new InvalidOperationException("Failed to reply card message: invalid response");
    }

    public async Task<FeishuStreamingHandle> CreateStreamingHandleAsync(
        string chatId,
        string? replyMessageId,
        string initialContent,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 创建卡片
        var cardId = await CreateCardAsync(initialContent, title, cancellationToken);

        // 2. 发送或回复卡片消息
        string messageId;
        if (!string.IsNullOrEmpty(replyMessageId))
        {
            messageId = await ReplyCardMessageAsync(replyMessageId, cardId, cancellationToken);
        }
        else
        {
            messageId = await SendCardMessageAsync(chatId, cardId, cancellationToken);
        }

        // 3. 创建流式句柄
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
    /// 获取或刷新访问令牌
    /// 使用 SemaphoreSlim 实现异步安全的双重检查锁定
    /// </summary>
    private async Task<string> EnsureTokenAsync(CancellationToken cancellationToken)
    {
        // 快速路径：token 有效直接返回
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查
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
                // 回退：使用上次有效的 token（如果还有效）
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
                _lastValidToken = newToken; // 保存有效 token 用于回退

                _logger.LogDebug("Access token refreshed, expires at {ExpiresAt}", _tokenExpiresAt);
                return _accessToken;
            }

            // 解析失败但可能还有旧 token 可用
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

    /// <summary>
    /// 发送原始JSON卡片消息（帮助功能专用）
    /// 通过 CardKit 创建卡片，避免JSON格式问题
    /// </summary>
    public async Task<string> SendRawCardAsync(
        string chatId,
        string cardJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📤 [FeishuCardKit] 通过CardKit发送卡片");

        // 1. 先用 CardKit API 创建卡片
        var token = await EnsureTokenAsync(cancellationToken);

        var createCardPayload = new
        {
            type = "card_json",
            data = cardJson
        };

        var createResponse = await PostAsync(
            "/open-apis/cardkit/v1/cards",
            token,
            createCardPayload,
            cancellationToken);

        var createResult = await ParseResponseAsync(createResponse, cancellationToken);

        if (!createResult.TryGetProperty("data", out var createData) ||
            !createData.TryGetProperty("card_id", out var cardIdProp))
        {
            throw new InvalidOperationException("Failed to create card via CardKit");
        }

        var cardId = cardIdProp.GetString() ?? string.Empty;
        _logger.LogInformation("📤 [FeishuCardKit] CardKit创建成功: CardId={CardId}", cardId);

        // 2. 再发送卡片消息
        return await SendCardMessageAsync(chatId, cardId, cancellationToken);
    }

    /// <summary>
    /// 回复原始JSON卡片消息(帮助功能专用)
    /// 参考 OpenCowork 实现:先创建卡片获取 card_id,再发送
    /// </summary>
    public async Task<string> ReplyRawCardAsync(
        string replyMessageId,
        string cardJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📤 [FeishuCardKit] 回复交互式卡片, ReplyMessageId={ReplyMessageId}", replyMessageId);
        _logger.LogDebug("📤 [FeishuCardKit] 卡片JSON: {CardJson}", cardJson);

        try
        {
            var token = await EnsureTokenAsync(cancellationToken);

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
                cancellationToken);

            var createContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("📤 [FeishuCardKit] 创建卡片响应: {Response}", createContent);

            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogError("❌ [FeishuCardKit] 创建卡片失败: {Content}", createContent);
                throw new HttpRequestException($"Create card failed: {createResponse.StatusCode}");
            }

            var createResult = JsonDocument.Parse(createContent).RootElement;

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
                cancellationToken);

            var replyContent = await replyResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("📤 [FeishuCardKit] 回复消息响应: {Response}", replyContent);

            if (!replyResponse.IsSuccessStatusCode)
            {
                _logger.LogError("❌ [FeishuCardKit] 回复消息失败: {Content}", replyContent);
                throw new HttpRequestException($"Reply message failed: {replyResponse.StatusCode}");
            }

            var replyResult = JsonDocument.Parse(replyContent).RootElement;

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
}
