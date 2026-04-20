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
/// 椋炰功 CardKit 瀹㈡埛绔疄鐜?
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
        return await CreateCardCoreAsync(
            initialContent,
            title ?? effectiveOptions.DefaultCardTitle,
            cancellationToken,
            effectiveOptions,
            chrome: null);
    }

    public async Task<bool> UpdateCardAsync(
        string cardId,
        string content,
        int sequence,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        return await UpdateCardCoreAsync(
            cardId,
            content,
            sequence,
            title: null,
            cancellationToken,
            effectiveOptions,
            chrome: null);
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
        _logger.LogInformation("馃摛 [FeishuCardKit] ReplyCardMessageAsync: ReplyMessageId={ReplyMessageId}, CardId={CardId}",
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

        _logger.LogInformation("馃摛 [FeishuCardKit] 鍙戦€?POST 璇锋眰鍒?/open-apis/im/v1/messages/{ReplyMessageId}/reply", replyMessageId);
        var response = await PostAsync(
            $"/open-apis/im/v1/messages/{replyMessageId}/reply",
            token,
            payload,
            effectiveOptions,
            cancellationToken);

        _logger.LogInformation("馃摛 [FeishuCardKit] 鍝嶅簲鐘舵€佺爜: {StatusCode}", response.StatusCode);
        var result = await ParseResponseAsync(response, cancellationToken);
        EnsureBusinessSuccess(result, "Reply Feishu card message");
        _logger.LogDebug("馃摛 [FeishuCardKit] 鍝嶅簲鍐呭: {Response}", result);
        var messageId = ExtractMessageId(result, "reply card message");
        _logger.LogInformation("鉁?[FeishuCardKit] 鍥炲鎴愬姛, MessageId={MessageId}", messageId);
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
        FeishuOptions? optionsOverride = null,
        FeishuStreamingCardChrome? chrome = null)
    {
        var effectiveOptions = GetEffectiveOptions(optionsOverride);
        var cardTitle = title ?? effectiveOptions.DefaultCardTitle;

        // 1. 鍒涘缓鍗＄墖
        var cardId = await CreateCardCoreAsync(
            initialContent,
            cardTitle,
            cancellationToken,
            effectiveOptions,
            chrome);

        // 2. 鍙戦€佹垨鍥炲鍗＄墖娑堟伅
        string messageId;
        if (!string.IsNullOrEmpty(replyMessageId))
        {
            messageId = await ReplyCardMessageAsync(replyMessageId, cardId, cancellationToken, effectiveOptions);
        }
        else
        {
            messageId = await SendCardMessageAsync(chatId, cardId, cancellationToken, effectiveOptions);
        }

        // 3. 鍒涘缓娴佸紡鍙ユ焺
        return new FeishuStreamingHandle(
            cardId,
            messageId,
            (content, sequence) => UpdateCardCoreAsync(cardId, content, sequence, cardTitle, cancellationToken, effectiveOptions, chrome),
            (content, sequence) => UpdateCardCoreAsync(cardId, content, sequence, cardTitle, cancellationToken, effectiveOptions, chrome),
            effectiveOptions.StreamingThrottleMs
        );
    }

    private async Task<string> CreateCardCoreAsync(
        string initialContent,
        string title,
        CancellationToken cancellationToken,
        FeishuOptions effectiveOptions,
        FeishuStreamingCardChrome? chrome)
    {
        var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);
        var cardData = BuildStreamingCardData(initialContent, title, chrome, includeHeader: true);

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

    private async Task<bool> UpdateCardCoreAsync(
        string cardId,
        string content,
        int sequence,
        string? title,
        CancellationToken cancellationToken,
        FeishuOptions effectiveOptions,
        FeishuStreamingCardChrome? chrome)
    {
        try
        {
            var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);
            var cardData = BuildStreamingCardData(content, title, chrome, includeHeader: !string.IsNullOrWhiteSpace(title));

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

    private object BuildStreamingCardData(
        string content,
        string? title,
        FeishuStreamingCardChrome? chrome,
        bool includeHeader)
    {
        var body = new
        {
            elements = BuildStreamingCardElements(content, chrome)
        };

        if (includeHeader)
        {
            return new
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
                        content = title ?? _defaultOptions.DefaultCardTitle
                    }
                },
                body
            };
        }

        return new
        {
            schema = "2.0",
            config = new
            {
                update_multi = true,
                streaming_mode = true
            },
            body
        };
    }

    private object[] BuildStreamingCardElements(string content, FeishuStreamingCardChrome? chrome)
    {
        if (chrome == null)
        {
            return
            [
                new
                {
                    tag = "markdown",
                    content
                }
            ];
        }

        var hasStatusSection = !string.IsNullOrWhiteSpace(chrome.StatusMarkdown) || chrome.OverflowOptions.Count > 0;
        var hasBottomActions = chrome.BottomActions.Count > 0;
        if (!hasStatusSection && !hasBottomActions)
        {
            return
            [
                new
                {
                    tag = "markdown",
                    content
                }
            ];
        }

        var elements = new List<object>();
        if (hasStatusSection)
        {
            var statusMarkdown = string.IsNullOrWhiteSpace(chrome.StatusMarkdown)
                ? "当前会话"
                : chrome.StatusMarkdown;

            if (chrome.OverflowOptions.Count > 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new
                    {
                        tag = "lark_md",
                        content = statusMarkdown
                    },
                    extra = new
                    {
                        tag = "overflow",
                        options = BuildOverflowOptions(chrome.OverflowOptions)
                    }
                });
            }
            else
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new
                    {
                        tag = "lark_md",
                        content = statusMarkdown
                    }
                });
            }

            elements.Add(new { tag = "hr" });
        }

        elements.Add(new
        {
            tag = "markdown",
            content
        });

        if (hasBottomActions)
        {
            elements.Add(new { tag = "hr" });
            elements.Add(new
            {
                tag = "action",
                layout = "flow",
                actions = BuildBottomActions(chrome.BottomActions)
            });
        }

        return elements.ToArray();
    }

    private object[] BuildOverflowOptions(IEnumerable<FeishuStreamingCardOverflowOption> options)
    {
        return options
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select(option => (object)new
            {
                text = new
                {
                    tag = "plain_text",
                    content = option.Text
                },
                value = JsonSerializer.Serialize(option.Value)
            })
            .ToArray();
    }

    private object[] BuildBottomActions(IEnumerable<FeishuStreamingCardBottomAction> actions)
    {
        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Text))
            .Select(action => (object)new
            {
                tag = "button",
                text = new
                {
                    tag = "plain_text",
                    content = action.Text
                },
                type = string.IsNullOrWhiteSpace(action.Type) ? "default" : action.Type,
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = action.Value
                    }
                }
            })
            .ToArray();
    }

    private string ExtractMessageId(JsonElement result, string operationName)
    {
        if (result.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageIdProp))
        {
            return messageIdProp.GetString() ?? string.Empty;
        }

        _logger.LogError("鉂?[FeishuCardKit] 鍝嶅簲涓病鏈?message_id, Operation={Operation}", operationName);
        throw new InvalidOperationException($"Failed to {operationName}: invalid response");
    }

    /// <summary>
    /// 鑾峰彇鎴栧埛鏂拌闂护鐗?
    /// 浣跨敤 SemaphoreSlim 瀹炵幇寮傛瀹夊叏鐨勫弻閲嶆鏌ラ攣瀹?
    /// </summary>
    private async Task<string> EnsureTokenAsync(FeishuOptions options, CancellationToken cancellationToken)
    {
        var cacheEntry = GetTokenCacheEntry(options);

        // 蹇€熻矾寰勶細token 鏈夋晥鐩存帴杩斿洖
        if (!string.IsNullOrEmpty(cacheEntry.AccessToken) && DateTime.UtcNow < cacheEntry.TokenExpiresAt)
        {
            return cacheEntry.AccessToken;
        }

        await cacheEntry.TokenLock.WaitAsync(cancellationToken);
        try
        {
            // 鍙岄噸妫€鏌?
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
                // 鍥為€€锛氫娇鐢ㄤ笂娆℃湁鏁堢殑 token锛堝鏋滆繕鏈夋晥锛?
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

            // 瑙ｆ瀽澶辫触浣嗗彲鑳借繕鏈夋棫 token 鍙敤
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
    /// 鍙戦€佸師濮婮SON鍗＄墖娑堟伅锛堝府鍔╁姛鑳戒笓鐢級
    /// 閫氳繃 CardKit 鍒涘缓鍗＄墖锛岄伩鍏岼SON鏍煎紡闂
    /// </summary>
    public async Task<string> SendRawCardAsync(
        string chatId,
        string cardJson,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        _logger.LogInformation("馃摛 [FeishuCardKit] 閫氳繃 CardKit 鍙戦€佸崱鐗?");

        // 1. 鍏堢敤 CardKit API 鍒涘缓鍗＄墖
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
        _logger.LogInformation("馃摛 [FeishuCardKit] CardKit鍒涘缓鎴愬姛: CardId={CardId}", cardId);

        // 2. 鍐嶅彂閫佸崱鐗囨秷鎭?
        return await SendCardMessageAsync(chatId, cardId, cancellationToken, effectiveOptions);
    }

    /// <summary>
    /// 鍥炲 V2 DTO 鍗＄墖娑堟伅锛堝府鍔╁姛鑳戒笓鐢級
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
    /// 鍥炲鍘熷JSON鍗＄墖娑堟伅(甯姪鍔熻兘涓撶敤)
    /// 鍙傝€?OpenCowork 瀹炵幇:鍏堝垱寤哄崱鐗囪幏鍙?card_id,鍐嶅彂閫?
    /// </summary>
    public async Task<string> ReplyRawCardAsync(
        string replyMessageId,
        string cardJson,
        CancellationToken cancellationToken = default,
        FeishuOptions? optionsOverride = null)
    {
        _logger.LogInformation("馃摛 [FeishuCardKit] 鍥炲浜や簰寮忓崱鐗? ReplyMessageId={ReplyMessageId}", replyMessageId);
        _logger.LogDebug("馃摛 [FeishuCardKit] 鍗＄墖JSON: {CardJson}", cardJson);

        try
        {
            var effectiveOptions = GetEffectiveOptions(optionsOverride);
            var token = await EnsureTokenAsync(effectiveOptions, cancellationToken);

            // 姝ラ1: 浣跨敤 CardKit API 鍒涘缓鍗＄墖,鑾峰彇 card_id
            _logger.LogInformation("馃摛 [FeishuCardKit] 姝ラ1: 鍒涘缓鍗＄墖...");

            var createCardPayload = new
            {
                type = "card_json",
                data = cardJson  // cardJson 鏄瓧绗︿覆
            };

            var createResponse = await PostAsync(
                "/open-apis/cardkit/v1/cards",
                token,
                createCardPayload,
                effectiveOptions,
                cancellationToken);

            var createContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("馃摛 [FeishuCardKit] 鍒涘缓鍗＄墖鍝嶅簲: {Response}", createContent);

            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogError("鉂?[FeishuCardKit] 鍒涘缓鍗＄墖澶辫触: {Content}", createContent);
                throw new HttpRequestException($"Create card failed: {createResponse.StatusCode}");
            }

            var createResult = JsonDocument.Parse(createContent).RootElement;
            EnsureBusinessSuccess(createResult, "Create reply CardKit card");

            if (!createResult.TryGetProperty("data", out var createData) ||
                !createData.TryGetProperty("card_id", out var cardIdProp))
            {
                _logger.LogError("鉂?[FeishuCardKit] 鍝嶅簲涓病鏈?card_id");
                throw new InvalidOperationException("Failed to get card_id from response");
            }

            var cardId = cardIdProp.GetString() ?? string.Empty;
            _logger.LogInformation("馃摛 [FeishuCardKit] 姝ラ1: 鍗＄墖鍒涘缓鎴愬姛, CardId={CardId}", cardId);

            // 姝ラ2: 浣跨敤娑堟伅 API 鍥炲鍗＄墖(鍙戦€?card_id)
            _logger.LogInformation("馃摛 [FeishuCardKit] 姝ラ2: 鍥炲鍗＄墖娑堟伅...");

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
            _logger.LogDebug("馃摛 [FeishuCardKit] 鍥炲娑堟伅鍝嶅簲: {Response}", replyContent);

            if (!replyResponse.IsSuccessStatusCode)
            {
                _logger.LogError("鉂?[FeishuCardKit] 鍥炲娑堟伅澶辫触: {Content}", replyContent);
                throw new HttpRequestException($"Reply message failed: {replyResponse.StatusCode}");
            }

            var replyResult = JsonDocument.Parse(replyContent).RootElement;
            EnsureBusinessSuccess(replyResult, "Reply raw Feishu card message");

            if (replyResult.TryGetProperty("data", out var data) &&
                data.TryGetProperty("message_id", out var messageIdProp))
            {
                var messageId = messageIdProp.GetString() ?? string.Empty;
                _logger.LogInformation("鉁?[FeishuCardKit] 鍗＄墖鍥炲鎴愬姛, MessageId={MessageId}", messageId);
                return messageId;
            }

            _logger.LogError("鉂?[FeishuCardKit] 鍝嶅簲涓病鏈?message_id");
            throw new InvalidOperationException("Failed to get message_id from response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "鉂?[FeishuCardKit] ReplyRawCardAsync 澶辫触");
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

