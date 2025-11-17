using System.IO;
using System.Runtime.Versioning;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.DisplayDetection.Windows;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;
using LGTVSwitcher.Sandbox.Windows;
using LGTVSwitcher.Sandbox.Windows.DisplayDetection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Display detection prototype currently runs on Windows only.");
    return;
}

#pragma warning disable CA1416 // プラットフォーム互換性の検証警告を抑制
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) => ConfigureServices(context.Configuration, services))
    .UseConsoleLifetime()
    .Build();

await host.RunAsync().ConfigureAwait(false);

[SupportedOSPlatform("windows")]
static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
{
    // 1. 設定オブジェクトを DI に登録
    services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

    // 2. ClientKey を appsettings.json に永続化するストアを登録
    services.AddSingleton<ILgTvClientKeyStore>(sp =>
    {
        // HostBuilder が appsettings.json のパスを解決してくれる
        var hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
        var settingsPath = Path.Combine(hostEnvironment.ContentRootPath, "appsettings.json");

        var logger = sp.GetRequiredService<ILogger<FileBasedLgTvClientKeyStore>>();
        return new FileBasedLgTvClientKeyStore(settingsPath, logger);
    });

    // 3. LG TV クライアント関連のサービスを登録
    services.AddSingleton<ILgTvTransport, DefaultWebSocketTransport>();
    services.AddSingleton<ILgTvController, LgTvController>(); // ILgTvClientKeyStore は自動で注入される

    // 4. Windows ディスプレイ検知関連のサービスを登録
    services.AddSingleton<WindowsMessagePump>();
    services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
    services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();

    // 5. 常駐ワーカー（IHostedService）を登録
    // モニタ状態を LG TV と同期するワーカー
    services.AddHostedService<DisplaySyncWorker>();
    // モニタ変更をコンソールにログ出力するサンドボックス用ワーカー
    services.AddHostedService<DisplayChangeLoggingService>();
}
#pragma warning restore CA1416