using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 本地化服务实现
/// </summary>
[ServiceDescription(typeof(ILocalizationService), ServiceLifetime.Scoped)]
public class LocalizationService : ILocalizationService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IWebHostEnvironment? _webHostEnvironment;
    private readonly string _resourcePath = Path.Combine("Resources", "Localization");
    private readonly Dictionary<string, Dictionary<string, object>> _translationsCache = new();
    private string _currentLanguage = "zh-CN";

    public LocalizationService(IJSRuntime jsRuntime, IWebHostEnvironment? webHostEnvironment = null)
    {
        _jsRuntime = jsRuntime;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<string> GetCurrentLanguageAsync()
    {
        try
        {
            // 确保本地化初始化完成（会从 IndexedDB 读取语言）
            var language = await _jsRuntime.InvokeAsync<string>("localizationHelper.init");
            if (!string.IsNullOrEmpty(language))
            {
                _currentLanguage = language;
            }
            return _currentLanguage;
        }
        catch
        {
            return _currentLanguage;
        }
    }

    public async Task SetCurrentLanguageAsync(string language)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localizationHelper.setCurrentLanguage", language);
            _currentLanguage = language;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"设置语言失败: {ex.Message}");
        }
    }

    public async Task<string> GetTranslationAsync(string key, string? language = null)
    {
        return await GetTranslationAsync(key, new Dictionary<string, string>(), language);
    }

    public async Task<string> GetTranslationAsync(string key, Dictionary<string, string> parameters, string? language = null)
    {
        try
        {
            var lang = language ?? _currentLanguage;
            var translations = await GetAllTranslationsAsync(lang);

            // 支持嵌套键（如 "common.save"）
            var keys = key.Split('.');
            object? value = translations;

            foreach (var k in keys)
            {
                if (value is Dictionary<string, object> dict && dict.ContainsKey(k))
                {
                    value = dict[k];
                }
                else if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    if (element.TryGetProperty(k, out var nestedElement))
                    {
                        value = nestedElement;
                    }
                    else
                    {
                        return key; // 找不到翻译，返回键本身
                    }
                }
                else
                {
                    return key; // 找不到翻译，返回键本身
                }
            }

            var stringValue = value switch
            {
                string str => str,
                JsonElement stringElement when stringElement.ValueKind == JsonValueKind.String => stringElement.GetString(),
                _ => null
            };

            if (stringValue is null)
            {
                return key;
            }

            // 参数替换
            foreach (var param in parameters)
            {
                stringValue = stringValue.Replace($"{{{param.Key}}}", param.Value);
            }

            return stringValue;
        }
        catch
        {
            return key;
        }
    }

    public async Task<Dictionary<string, object>> GetAllTranslationsAsync(string language)
    {
        // 检查缓存
        if (_translationsCache.ContainsKey(language))
        {
            return _translationsCache[language];
        }

        var localTranslations = await TryLoadTranslationsFromFileAsync(language);
        if (localTranslations != null)
        {
            _translationsCache[language] = localTranslations;
            await TrySyncTranslationsToJsAsync(language, localTranslations);
            Console.WriteLine($"[LocalizationService] 已从本地文件加载 {language} 翻译资源");
            return localTranslations;
        }

        // 重试逻辑，处理网络延迟和 JS 未就绪的情况
        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                // 使用 JavaScript fetch 来加载翻译文件
                var filePath = $"./Resources/Localization/{language}.json";
                var json = await _jsRuntime.InvokeAsync<string>("localizationHelper.fetchTranslationFile", filePath);
                
                if (string.IsNullOrEmpty(json))
                {
                    Console.WriteLine($"翻译文件为空 ({language})，第 {retry + 1} 次尝试");
                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(300); // 等待后重试
                        continue;
                    }
                    return new Dictionary<string, object>();
                }
                
                var translations = DeserializeTranslations(json);

                _translationsCache[language] = translations;
                
                // 同时加载到 JS 端
                await TrySyncTranslationsToJsAsync(language, translations);

                Console.WriteLine($"[LocalizationService] 成功加载 {language} 翻译资源");
                return translations;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载翻译资源失败 ({language})，第 {retry + 1} 次尝试: {ex.Message}");
                if (retry < maxRetries - 1)
                {
                    await Task.Delay(300); // 等待后重试
                }
            }
        }

        Console.WriteLine($"[LocalizationService] 加载翻译资源失败 ({language})，已达最大重试次数");
        return new Dictionary<string, object>();
    }

    public List<LanguageInfo> GetSupportedLanguages()
    {
        return new List<LanguageInfo>
        {
            new() { Code = "zh-CN", Name = "Chinese Simplified", NativeName = "简体中文" },
            new() { Code = "en-US", Name = "English", NativeName = "English" },
            new() { Code = "ja-JP", Name = "Japanese", NativeName = "日本語" },
            new() { Code = "ko-KR", Name = "Korean", NativeName = "한국어" }
        };
    }

    public async Task ReloadTranslationsAsync()
    {
        _translationsCache.Clear();
        await GetAllTranslationsAsync(_currentLanguage);
    }

    private async Task<Dictionary<string, object>?> TryLoadTranslationsFromFileAsync(string language)
    {
        try
        {
            var webRootPath = _webHostEnvironment?.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                return null;
            }

            var filePath = Path.Combine(webRootPath, _resourcePath, $"{language}.json");
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, object>();
            }

            return DeserializeTranslations(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] 读取本地翻译文件失败 ({language}): {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, object> DeserializeTranslations(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new Dictionary<string, object>();
    }

    private async Task TrySyncTranslationsToJsAsync(string language, Dictionary<string, object> translations)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localizationHelper.loadTranslations", language, translations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] 同步 {language} 翻译到 JS 失败: {ex.Message}");
        }
    }
}

