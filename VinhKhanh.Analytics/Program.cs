using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => logging.AddConsole())
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<VinhKhanh.Analytics.AnalyticsBackgroundService>();
        services.AddHostedService<VinhKhanh.Analytics.AudioProcessingService>();
    })
    .Build()
    .Run();
