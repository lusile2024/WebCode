using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WebCodeCli.Domain.Domain.Service;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace WebCodeCli.Controllers;

internal static partial class PreviewHtmlRewrite
{
    public static string Rewrite(string html, string basePath)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        basePath = NormalizeBasePath(basePath);

        // 1) 注入 base 标签（对相对路径资源/路由有效）
        if (!html.Contains("<base", StringComparison.OrdinalIgnoreCase) &&
            html.Contains("<head", StringComparison.OrdinalIgnoreCase))
        {
            var baseTag = $"<base href=\"{basePath}/\">";
            html = html.Replace("<head>", $"<head>\n    {baseTag}", StringComparison.OrdinalIgnoreCase);
        }

        // 2) 关键：重写根路径静态资源（例如 Vite 构建产物常见的 /assets/...）。
        // <base> 对以 '/' 开头的绝对路径无效，所以必须改写这些 URL。
        html = RootRelativeAssetAttributeRegex().Replace(html, match =>
        {
            var prefix = match.Groups["prefix"].Value; // src= 或 href=
            var quote = match.Groups["q"].Value;
            var path = match.Groups["path"].Value;
            return $"{prefix}{quote}{basePath}/{path}{quote}";
        });

        // 3) 兼容少量内联样式/标签中 url(/assets/...) 的情况
        html = CssRootRelativeAssetUrlRegex().Replace(html, match =>
        {
            var path = match.Groups["path"].Value;
            return $"url({basePath}/{path})";
        });

        return html;
    }

    private static string NormalizeBasePath(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        basePath = basePath.Trim();
        if (!basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        return basePath.TrimEnd('/');
    }

    [GeneratedRegex(@"(?<prefix>\b(?:src|href)=)(?<q>['""])/(?<path>(?:assets/|src/|node_modules/|@vite/|@react-refresh|favicon\.(?:ico|png|svg)|manifest\.json|robots\.txt|vite\.svg)[^'""#?]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RootRelativeAssetAttributeRegex();

    [GeneratedRegex(@"url\(\s*/(?<path>(?:assets/|src/)[^)\s]+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CssRootRelativeAssetUrlRegex();
}

/// <summary>
/// 自定义路径转换器，用于重写代理路径和响应内容
/// </summary>
public class CustomPathTransformer : HttpTransformer
{
    private readonly string _targetPath;
    private readonly string _basePath;
    private readonly ILogger<CustomPathTransformer>? _logger;

    public CustomPathTransformer(string targetPath, string basePath, ILogger<CustomPathTransformer>? logger = null)
    {
        // 规范化路径：确保以 / 开头，但不重复
        if (string.IsNullOrEmpty(targetPath) || targetPath == "/")
        {
            _targetPath = "/";
        }
        else
        {
            _targetPath = targetPath.StartsWith('/') ? targetPath : $"/{targetPath}";
        }
        _basePath = basePath;
        _logger = logger;
    }

    public override async ValueTask TransformRequestAsync(HttpContext httpContext, 
        HttpRequestMessage proxyRequest, string destinationPrefix, 
        CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
        
        // 构建目标URL，确保路径正确拼接
        // destinationPrefix 已经包含了 http://localhost:port
        // _targetPath 是要转发的路径，如 /assets/index.js
        var targetUrl = destinationPrefix.TrimEnd('/') + _targetPath + httpContext.Request.QueryString;
        proxyRequest.RequestUri = new Uri(targetUrl);
        
        _logger?.LogInformation("YARP转发: {OriginalPath} -> {TargetUrl}", 
            httpContext.Request.Path, targetUrl);
        
        // 确保Host header正确
        proxyRequest.Headers.Host = null;
    }

    public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, 
        HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
    {
        if (proxyResponse != null)
        {
            var mediaType = proxyResponse.Content.Headers.ContentType?.MediaType;

            // 1) HTML：重写 base 与根路径资源
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await proxyResponse.Content.ReadAsStringAsync(cancellationToken);
                html = PreviewHtmlRewrite.Rewrite(html, _basePath);

                var bytes = Encoding.UTF8.GetBytes(html);
                httpContext.Response.ContentLength = bytes.Length;
                httpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                httpContext.Response.Headers["Pragma"] = "no-cache";
                httpContext.Response.Headers["Expires"] = "0";
                await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);

                return true; // 跳过默认的响应复制
            }

            // 2) JS：重写根路径导入（/src /node_modules /@vite 等）
            if (string.Equals(mediaType, "application/javascript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "text/javascript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "application/x-javascript", StringComparison.OrdinalIgnoreCase))
            {
                var js = await proxyResponse.Content.ReadAsStringAsync(cancellationToken);

                js = RewriteRootPathsInJs(js, _basePath);

                var bytes = Encoding.UTF8.GetBytes(js);
                httpContext.Response.ContentLength = bytes.Length;
                httpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                httpContext.Response.Headers["Pragma"] = "no-cache";
                httpContext.Response.Headers["Expires"] = "0";
                await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);

                return true;
            }

            // 3) CSS：重写 url(/xxx) 资源
            if (string.Equals(mediaType, "text/css", StringComparison.OrdinalIgnoreCase))
            {
                var css = await proxyResponse.Content.ReadAsStringAsync(cancellationToken);

                css = RewriteRootPathsInCss(css, _basePath);

                var bytes = Encoding.UTF8.GetBytes(css);
                httpContext.Response.ContentLength = bytes.Length;
                httpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                httpContext.Response.Headers["Pragma"] = "no-cache";
                httpContext.Response.Headers["Expires"] = "0";
                await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);

                return true;
            }
        }
        
        // 其他类型交给默认处理
        return await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
    }

    private static string RewriteRootPathsInJs(string js, string basePath)
    {
        if (string.IsNullOrEmpty(js) || string.IsNullOrEmpty(basePath))
            return js;

        // 处理 import / fetch / new URL 等字符串常量中的根路径
        // 匹配: "/src/xxx" "/node_modules/xxx" "/@vite/client" "/@react-refresh"
        var pattern = @"(['""`])/(src/|node_modules/|@vite/|@react-refresh/)([^'""`]+)";
        return Regex.Replace(js, pattern, m =>
        {
            var quote = m.Groups[1].Value;
            var path = m.Groups[2].Value + m.Groups[3].Value;
            return $"{quote}{basePath}/{path}";
        }, RegexOptions.IgnoreCase);
    }

    private static string RewriteRootPathsInCss(string css, string basePath)
    {
        if (string.IsNullOrEmpty(css) || string.IsNullOrEmpty(basePath))
            return css;

        // 处理 url(/src/xxx) 或 url(/assets/xxx) 等
        var pattern = @"url\(\s*/(?<path>(src/|assets/|node_modules/|@vite/)[^\)\s]+)\s*\)";
        return Regex.Replace(css, pattern, m =>
        {
            var path = m.Groups["path"].Value;
            return $"url({basePath}/{path})";
        }, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// 前端预览反向代理控制器
/// </summary>
[ApiController]
[Route("api/preview")]
[Authorize]
public class PreviewProxyController : ControllerBase
{
    private readonly IDevServerManager _devServerManager;
    private readonly IHttpForwarder _forwarder;
    private readonly ILogger<PreviewProxyController> _logger;
    private readonly HttpMessageInvoker _httpClient;

    public PreviewProxyController(
        IDevServerManager devServerManager,
        IHttpForwarder forwarder,
        ILogger<PreviewProxyController> logger)
    {
        _devServerManager = devServerManager;
        _forwarder = forwarder;
        _logger = logger;
        
        // 创建用于转发的 HttpClient
        _httpClient = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });
    }

    /// <summary>
    /// 代理到开发服务器
    /// GET /api/preview/{sessionId}/{serverKey}/**
    /// </summary>
    [HttpGet("{sessionId}/{serverKey}")]
    [HttpPost("{sessionId}/{serverKey}")]
    [HttpPut("{sessionId}/{serverKey}")]
    [HttpPatch("{sessionId}/{serverKey}")]
    [HttpHead("{sessionId}/{serverKey}")]
    [HttpOptions("{sessionId}/{serverKey}")]
    [HttpGet("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpPost("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpPut("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpDelete("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpPatch("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpHead("{sessionId}/{serverKey}/{*catchAll}")]
    [HttpOptions("{sessionId}/{serverKey}/{*catchAll}")]
    public async Task ProxyRequest(string sessionId, string serverKey, string? catchAll = "")
    {
        try
        {
            // 获取服务器信息
            var serverInfo = await _devServerManager.GetServerInfoAsync(sessionId, serverKey);
            
            if (serverInfo == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                await Response.WriteAsJsonAsync(new { error = "服务器不存在" });
                return;
            }

            if (serverInfo.Status != Domain.Domain.Model.DevServerStatus.Running)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await Response.WriteAsJsonAsync(new 
                { 
                    error = "服务器未运行", 
                    status = serverInfo.Status.ToString() 
                });
                return;
            }

            // 构建目标基础URL
            var destinationPrefix = $"http://localhost:{serverInfo.Port}";
            
            // 构建basePath用于HTML base标签
            var basePath = $"/api/preview/{sessionId}/{serverKey}";
            
            // 规范化catchAll路径
            var normalizedPath = string.IsNullOrEmpty(catchAll) ? "" : catchAll;
            
            _logger.LogInformation("Proxying request from {SourcePath} to {Destination}/{CatchAll}", 
                Request.Path, destinationPrefix, normalizedPath);

            // 对于HTML请求，使用特殊处理来注入base标签
            if (string.IsNullOrEmpty(catchAll) || catchAll.EndsWith(".html") || !catchAll.Contains('.'))
            {
                _logger.LogInformation("检测为HTML请求，直接处理: {CatchAll}", catchAll ?? "(empty)");
                
                // 可能是HTML，直接获取并修改
                using var httpClient = new HttpClient();
                // 正确拼接URL：确保路径部分以 / 开头
                var targetUrl = string.IsNullOrEmpty(normalizedPath) 
                    ? destinationPrefix 
                    : $"{destinationPrefix}/{normalizedPath}";
                
                _logger.LogInformation("请求HTML: {TargetUrl}", targetUrl);
                
                var response = await httpClient.GetAsync(targetUrl);
                
                _logger.LogInformation("HTML响应状态: {StatusCode}, ContentType: {ContentType}", 
                    response.StatusCode, response.Content.Headers.ContentType?.MediaType);
                
                if (response.IsSuccessStatusCode && 
                    response.Content.Headers.ContentType?.MediaType == "text/html")
                {
                    var html = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation("HTML内容长度: {Length}", html.Length);

                    html = PreviewHtmlRewrite.Rewrite(html, basePath);
                    
                    Response.ContentType = "text/html; charset=utf-8";
                    Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    Response.Headers["Pragma"] = "no-cache";
                    Response.Headers["Expires"] = "0";
                    await Response.WriteAsync(html);
                    return;
                }
                else
                {
                    _logger.LogWarning("不是HTML响应或请求失败，使用YARP代理");
                }
            }

            // 对于其他请求，使用YARP代理
            _logger.LogInformation("使用YARP代理静态资源: {CatchAll}", normalizedPath);
            
            // 创建logger工厂来为transformer提供logger
            var loggerFactory = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var transformerLogger = loggerFactory.CreateLogger<CustomPathTransformer>();
            
            var transformer = new CustomPathTransformer(catchAll ?? "", basePath, transformerLogger);

            // 使用 YARP 转发请求
            var error = await _forwarder.SendAsync(
                HttpContext,
                destinationPrefix,
                _httpClient,
                ForwarderRequestConfig.Empty,
                transformer);

            // 检查是否有错误
            if (error != ForwarderError.None)
            {
                var errorFeature = HttpContext.Features.Get<IForwarderErrorFeature>();
                var exception = errorFeature?.Exception;

                _logger.LogError(exception, "代理请求失败: {Error}, Path: {Path}, CatchAll: {CatchAll}", 
                    error, Request.Path, normalizedPath);

                Response.StatusCode = StatusCodes.Status502BadGateway;
                await Response.WriteAsJsonAsync(new 
                { 
                    error = "代理请求失败", 
                    details = exception?.Message,
                    path = Request.Path.ToString(),
                    catchAll = normalizedPath
                });
            }
            else
            {
                _logger.LogInformation("YARP代理成功: {CatchAll}, StatusCode: {StatusCode}", 
                    normalizedPath, Response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理代理请求时发生错误");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(new 
            { 
                error = "服务器错误", 
                message = ex.Message 
            });
        }
    }

    /// <summary>
    /// 获取服务器状态
    /// GET /api/preview/{sessionId}/{serverKey}/status
    /// </summary>
    [HttpGet("{sessionId}/{serverKey}/_status")]
    public async Task<IActionResult> GetServerStatus(string sessionId, string serverKey)
    {
        try
        {
            var serverInfo = await _devServerManager.GetServerInfoAsync(sessionId, serverKey);
            
            if (serverInfo == null)
            {
                return NotFound(new { error = "服务器不存在" });
            }

            return Ok(new
            {
                serverKey = serverInfo.ServerKey,
                status = serverInfo.Status.ToString(),
                port = serverInfo.Port,
                proxyUrl = serverInfo.ProxyUrl,
                startedAt = serverInfo.StartedAt,
                processId = serverInfo.ProcessId,
                errorMessage = serverInfo.ErrorMessage,
                recentLogs = serverInfo.RecentLogs.TakeLast(20).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取服务器状态失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取会话的所有服务器
    /// GET /api/preview/{sessionId}/_servers
    /// </summary>
    [HttpGet("{sessionId}/_servers")]
    public IActionResult GetSessionServers(string sessionId)
    {
        try
        {
            var servers = _devServerManager.GetSessionServers(sessionId);
            
            return Ok(servers.Select(s => new
            {
                serverKey = s.ServerKey,
                projectName = s.ProjectInfo.Name,
                projectType = s.ProjectInfo.Type.ToString(),
                status = s.Status.ToString(),
                port = s.Port,
                proxyUrl = s.ProxyUrl,
                isBuildMode = s.IsBuildMode,
                startedAt = s.StartedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话服务器列表失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 停止服务器
    /// DELETE /api/preview/{sessionId}/{serverKey}
    /// </summary>
    [HttpDelete("{sessionId}/{serverKey}")]
    public async Task<IActionResult> StopServer(string sessionId, string serverKey)
    {
        try
        {
            await _devServerManager.StopDevServerAsync(sessionId, serverKey);
            return Ok(new { message = "服务器已停止" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止服务器失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }
}

