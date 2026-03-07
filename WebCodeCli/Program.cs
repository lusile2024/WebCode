using Microsoft.AspNetCore.Components;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service;
using Serilog;
using Log = Serilog.Log;

var builder = WebApplication.CreateBuilder(args);

// 配置 Kestrel 服务器限制
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // 增加请求体大小限制到 100MB
    serverOptions.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
    // 增加请求头大小限制
    serverOptions.Limits.MaxRequestHeadersTotalSize = 64 * 1024; // 64KB
    // 增加连接超时时间
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    // 增加 SignalR 消息大小限制，解决大数据传输时连接断开的问题
    options.MaxBufferedUnacknowledgedRenderBatches = 100;
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(5);
    options.DetailedErrors = true;
})
.AddHubOptions(options =>
{
    // 增加 Hub 消息大小限制到 10MB
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
    options.EnableDetailedErrors = true;
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.HandshakeTimeout = TimeSpan.FromMinutes(1);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetService<NavigationManager>()!.BaseUri),
    Timeout = TimeSpan.FromMinutes(10) // 增加到 10 分钟，支持长时间运行的 CLI 工具
});
builder.Services.Configure<CliToolsOption>(builder.Configuration.GetSection("CliTools"));
builder.Services.Configure<WebCodeCli.Domain.Common.Options.AuthenticationOption>(builder.Configuration.GetSection("Authentication"));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddServicesFromAssemblies("WebCodeCli", "WebCodeCli.Domain");

// 添加 HttpClient 工厂（飞书 CardKit 客户端需要）
builder.Services.AddHttpClient();

// 添加飞书渠道服务
builder.Services.AddFeishuChannel(builder.Configuration);

// 添加 YARP 反向代理
builder.Services.AddHttpForwarder();

// 添加工作区清理后台服务
builder.Services.AddHostedService<WorkspaceCleanupBackgroundService>();

// 添加 Quartz 定时任务后台服务
// 先注册为 Singleton，然后作为 IHostedService
builder.Services.AddSingleton<QuartzHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QuartzHostedService>());

// 加载数据库配置
var dbConfig = builder.Configuration.GetSection("DBConnection").Get<DBConnectionOption>();
if (dbConfig != null)
{
    // 根据操作系统设置默认数据库路径
    if (string.IsNullOrEmpty(dbConfig.ConnectionStrings) || dbConfig.ConnectionStrings == "Data Source=WebCodeCli.db")
    {
        // Windows 使用当前目录，Linux 使用 /app/data 目录
        if (OperatingSystem.IsWindows())
        {
            dbConfig.ConnectionStrings = "Data Source=WebCodeCli.db";
        }
        else
        {
            dbConfig.ConnectionStrings = "Data Source=/app/data/WebCodeCli.db";
            
            // 确保 Linux 数据目录存在
            var dataDir = "/app/data";
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                Log.Information($"Created data directory: {dataDir}");
            }
        }
    }
    
    // 设置全局实例
    DBConnectionOption.Instance = dbConfig;
    
    Log.Information($"Database Type: {DBConnectionOption.Instance.DbType}");
    Log.Information($"Connection String: {DBConnectionOption.Instance.ConnectionStrings}");
}

builder.Configuration.GetSection("OpenAI").Get<OpenAIOption>();


// 添加响应压缩服务
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/javascript", "text/css", "text/javascript", "application/json", "text/html" });
});

// 添加 CORS 服务
builder.Services.AddCors(options =>
{
    options.AddPolicy("Any", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// 配置 Serilog 静态日志器
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/feishu-.log", rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// 将 Serilog 集成到 ASP.NET Core 日志系统
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);



var app = builder.Build();

// 初始化命令扫描服务
using (var scope = app.Services.CreateScope())
{
    var commandScanner = scope.ServiceProvider.GetRequiredService<CommandScannerService>();
    commandScanner.Initialize();
    Log.Information("✅ CommandScannerService 初始化完成，命令扫描和监听已启动");
}

// 启用响应压缩（必须在其他中间件之前）
app.UseResponseCompression();

// 启用 CORS
app.UseCors("Any");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();

app.MapFallbackToPage("/_Host");

app.UseAuthorization();

app.MapControllers();

app.CodeFirst();

app.Run();
