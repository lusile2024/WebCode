using System.Text.Json;
using Microsoft.AspNetCore.Components;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Pages;

public partial class Setup : ComponentBase
{
    [Inject] private ISystemSettingsService SystemSettingsService { get; set; } = default!;
    [Inject] private ICcSwitchService CcSwitchService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;

    private int _currentStep = 1;
    private bool _isLoading;
    private bool _isCompleted;
    private string _errorMessage = string.Empty;
    private string _defaultWorkspaceRoot = string.Empty;
    private CcSwitchStatus? _ccSwitchStatus;

    private bool _enableClaudeCode;
    private bool _enableCodex;
    private bool _enableOpenCode;

    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "zh-CN";

    private readonly SystemInitConfig _config = new()
    {
        EnableAuth = true,
        AdminUsername = "admin",
        AdminPassword = string.Empty,
        EnabledAssistants = new List<string>()
    };

    protected override async Task OnInitializedAsync()
    {
        var isInitialized = await SystemSettingsService.IsSystemInitializedAsync();
        if (isInitialized)
        {
            NavigationManager.NavigateTo("/", forceLoad: true);
            return;
        }

        _defaultWorkspaceRoot = await SystemSettingsService.GetWorkspaceRootAsync();

        try
        {
            _currentLanguage = await L.GetCurrentLanguageAsync();
            await LoadTranslationsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] 初始化本地化失败: {ex.Message}");
        }

        await RefreshCcSwitchStatusAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            if (_translations.Count == 0)
            {
                _currentLanguage = await L.GetCurrentLanguageAsync();
                await LoadTranslationsAsync();
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] 首次渲染后刷新本地化失败: {ex.Message}");
        }
    }

    private async Task RefreshCcSwitchStatusAsync()
    {
        try
        {
            _ccSwitchStatus = await CcSwitchService.GetStatusAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] 读取 cc-switch 状态失败: {ex.Message}");
            _ccSwitchStatus = new CcSwitchStatus
            {
                IsDetected = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GetStepClass(int step)
    {
        if (step < _currentStep)
        {
            return "w-8 h-8 rounded-full bg-gray-800 text-white flex items-center justify-center text-sm font-bold";
        }

        if (step == _currentStep)
        {
            return "w-8 h-8 rounded-full bg-gray-800 text-white flex items-center justify-center text-sm font-bold ring-4 ring-gray-300";
        }

        return "w-8 h-8 rounded-full bg-gray-200 text-gray-500 flex items-center justify-center text-sm font-bold";
    }

    private string GetStepTitle(int step)
    {
        return step switch
        {
            1 => T("setup.step1Title"),
            2 => T("setup.step2Title"),
            3 => T("setup.step3Title"),
            _ => string.Empty
        };
    }

    private async Task NextStepAsync()
    {
        _errorMessage = string.Empty;

        if (_currentStep == 1)
        {
            if (_config.EnableAuth)
            {
                if (string.IsNullOrWhiteSpace(_config.AdminUsername))
                {
                    _errorMessage = T("setup.validation.adminUsernameRequired");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_config.AdminPassword))
                {
                    _errorMessage = T("setup.validation.adminPasswordRequired");
                    return;
                }

                if (_config.AdminPassword.Length < 6)
                {
                    _errorMessage = T("setup.validation.passwordTooShort", ("min", "6"));
                    return;
                }
            }
        }

        if (_currentStep == 2)
        {
            await RefreshCcSwitchStatusAsync();
        }

        if (_currentStep < 3)
        {
            _currentStep++;
        }
    }

    private void PrevStep()
    {
        _errorMessage = string.Empty;
        if (_currentStep > 1)
        {
            _currentStep--;
        }
    }

    private async Task CompleteSetup()
    {
        _isLoading = true;
        _errorMessage = string.Empty;
        StateHasChanged();

        try
        {
            await RefreshCcSwitchStatusAsync();
            var enabledAssistants = GetEnabledAssistants();

            if (enabledAssistants.Any())
            {
                var blockedStatuses = enabledAssistants
                    .Select(GetToolStatus)
                    .Where(status => !status.IsLaunchReady)
                    .ToList();

                if (blockedStatuses.Count > 0)
                {
                    _errorMessage = blockedStatuses[0].StatusMessage
                        ?? T("setup.validation.assistantsNotReady");
                    return;
                }
            }

            _config.EnabledAssistants = enabledAssistants;

            var result = await SystemSettingsService.CompleteInitializationAsync(_config);
            if (result)
            {
                _isCompleted = true;
            }
            else
            {
                _errorMessage = T("setup.error.saveFailed");
            }
        }
        catch (Exception ex)
        {
            _errorMessage = T("setup.error.configFailed", ("message", ex.Message));
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private void GoToHome()
    {
        NavigationManager.NavigateTo("/", forceLoad: true);
    }

    private async Task HandleLanguageChanged(string languageCode)
    {
        _currentLanguage = languageCode;
        await LoadTranslationsAsync();
        StateHasChanged();
    }

    private List<string> GetEnabledAssistants()
    {
        var list = new List<string>();
        if (_enableClaudeCode)
        {
            list.Add("claude-code");
        }

        if (_enableCodex)
        {
            list.Add("codex");
        }

        if (_enableOpenCode)
        {
            list.Add("opencode");
        }

        return list;
    }

    private CcSwitchToolStatus GetToolStatus(string toolId)
    {
        if (_ccSwitchStatus?.Tools != null
            && _ccSwitchStatus.Tools.TryGetValue(toolId, out var status))
        {
            return status;
        }

        return new CcSwitchToolStatus
        {
            ToolId = toolId,
            ToolName = GetToolDisplayName(toolId),
            IsManaged = true,
            IsLaunchReady = false,
            StatusMessage = T("setup.validation.assistantsNotReady")
        };
    }

    private static string GetToolDisplayName(string toolId)
    {
        return toolId switch
        {
            "claude-code" => "Claude Code",
            "codex" => "Codex",
            "opencode" => "OpenCode",
            _ => toolId
        };
    }

    private string GetAssistantCardClass(bool isSelected)
    {
        return isSelected
            ? "bg-gray-50 border-gray-800"
            : "bg-white border-gray-200 hover:border-gray-400";
    }

    private string GetStatusBadgeClass(bool isReady)
    {
        return isReady
            ? "bg-green-100 text-green-700"
            : "bg-red-100 text-red-700";
    }

    private string DisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return T("setup.notAvailable");
        }

        var normalizedValue = value.Trim();
        return normalizedValue.Equals("notAvailable", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("not_available", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("unavailable", StringComparison.OrdinalIgnoreCase)
            ? T("setup.notAvailable")
            : normalizedValue;
    }

    private string DisplayProviderCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return T("setup.notAvailable");
        }

        var normalizedCategory = category.Trim().ToLowerInvariant();
        if (normalizedCategory is "notavailable" or "not_available" or "unavailable")
        {
            return T("setup.notAvailable");
        }

        var localizedValue = T($"setup.providerCategoryValues.{normalizedCategory}");
        return string.Equals(localizedValue, normalizedCategory, StringComparison.Ordinal)
            ? category
            : localizedValue;
    }

    #region Localization

    private async Task LoadTranslationsAsync()
    {
        try
        {
            var allTranslations = await L.GetAllTranslationsAsync(_currentLanguage);
            _translations = FlattenTranslations(allTranslations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] 加载翻译资源失败: {ex.Message}");
        }
    }

    private Dictionary<string, string> FlattenTranslations(Dictionary<string, object> source, string prefix = "")
    {
        var result = new Dictionary<string, string>();

        foreach (var kvp in source)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object)
                {
                    var nested = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
                    if (nested != null)
                    {
                        foreach (var item in FlattenTranslations(nested, key))
                        {
                            result[item.Key] = item.Value;
                        }
                    }
                }
                else if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    result[key] = jsonElement.GetString() ?? key;
                }
            }
            else if (kvp.Value is Dictionary<string, object> dict)
            {
                foreach (var item in FlattenTranslations(dict, key))
                {
                    result[item.Key] = item.Value;
                }
            }
            else if (kvp.Value is string str)
            {
                result[key] = str;
            }
        }

        return result;
    }

    private string T(string key)
    {
        if (_translations.TryGetValue(key, out var translation))
        {
            return translation;
        }

        var parts = key.Split('.');
        return parts.Length > 0 ? parts[^1] : key;
    }

    private string T(string key, params (string name, string value)[] parameters)
    {
        var text = T(key);
        foreach (var (name, value) in parameters)
        {
            text = text.Replace($"{{{name}}}", value);
        }

        return text;
    }

    #endregion
}
