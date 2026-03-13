using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Pages;

public partial class Login : ComponentBase
{
    [Inject] private IAuthenticationService AuthenticationService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private IUserContextService UserContextService { get; set; } = default!;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading = false;
    private bool _showPassword = false;

    // 本地化相关
    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "zh-CN";

    protected override async Task OnInitializedAsync()
    {
        // 初始化本地化
        try
        {
            _currentLanguage = await L.GetCurrentLanguageAsync();
            await LoadTranslationsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[登录] 初始化本地化失败: {ex.Message}");
        }

        // 如果认证未启用，直接跳转到主页
        if (!AuthenticationService.IsAuthenticationEnabled())
        {
            NavigationManager.NavigateTo("/code-assistant");
            return;
        }

        // 检查是否已登录
        var (isAuthenticated, username) = await CheckAuthenticationAsync();
        if (isAuthenticated && !string.IsNullOrEmpty(username))
        {
            // 已登录用户，设置用户名到上下文
            UserContextService.SetCurrentUsername(username);
            NavigationManager.NavigateTo("/code-assistant");
        }
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
            Console.WriteLine($"[登录] 首次渲染后刷新本地化失败: {ex.Message}");
        }
    }

    private async Task HandleLogin()
    {
        _errorMessage = string.Empty;

        // 验证输入
        if (string.IsNullOrWhiteSpace(_username))
        {
            _errorMessage = T("login.validation.usernameRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(_password))
        {
            _errorMessage = T("login.validation.passwordRequired");
            return;
        }

        _isLoading = true;
        StateHasChanged();

        // 模拟网络延迟，提升用户体验
        await Task.Delay(300);

        try
        {
            // 验证用户名和密码
            bool isValid = AuthenticationService.ValidateUser(_username, _password);

            if (isValid)
            {
                // 登录成功，保存认证状态到 SessionStorage
                await SaveAuthentication(_username);

                // 设置当前登录用户名到上下文，后续所有操作都使用这个用户名
                UserContextService.SetCurrentUsername(_username);

                // 跳转到主页
                NavigationManager.NavigateTo("/code-assistant", forceLoad: true);
            }
            else
            {
                _errorMessage = T("login.error.invalidCredentials");
                _password = string.Empty; // 清空密码
            }
        }
        catch (Exception ex)
        {
            _errorMessage = T("login.error.loginFailed", ("message", ex.Message));
            Console.WriteLine($"登录异常: {ex}");
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_isLoading)
        {
            await HandleLogin();
        }
    }

    private void TogglePasswordVisibility()
    {
        _showPassword = !_showPassword;
    }

    private async Task SaveAuthentication(string username)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "isAuthenticated", "true");
            await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "username", username);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存认证状态失败: {ex.Message}");
        }
    }

    private async Task<(bool IsAuthenticated, string? Username)> CheckAuthenticationAsync()
    {
        try
        {
            var isAuthenticated = await JSRuntime.InvokeAsync<string>("sessionStorage.getItem", "isAuthenticated");
            if (isAuthenticated == "true")
            {
                var username = await JSRuntime.InvokeAsync<string>("sessionStorage.getItem", "username");
                return (true, username);
            }
            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    #region 本地化辅助方法

    private async Task LoadTranslationsAsync()
    {
        try
        {
            var allTranslations = await L.GetAllTranslationsAsync(_currentLanguage);
            _translations = FlattenTranslations(allTranslations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[登录] 加载翻译资源失败: {ex.Message}");
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

