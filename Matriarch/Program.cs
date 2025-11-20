using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Matriarch.Configuration;
using Matriarch.Services;

namespace Matriarch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                // Bind and register configuration
                var appSettings = new AppSettings();
                context.Configuration.Bind(appSettings);
                
                // Validate configuration
                ValidateConfiguration(appSettings);
                
                // Register AppSettings as singleton
                services.AddSingleton(appSettings);

                // Register services
                services.AddSingleton<CachingService>();
                services.AddSingleton<AzureDataService>();
                services.AddSingleton<Neo4jService>();

                // Register the worker
                services.AddHostedService<MatriarchWorker>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.TimestampFormat = "HH:mm:ss ";
                });
            });

    private static void ValidateConfiguration(AppSettings settings)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.Azure.TenantId))
            errors.Add("Azure TenantId is not configured");
        if (string.IsNullOrWhiteSpace(settings.Azure.ClientId))
            errors.Add("Azure ClientId is not configured");
        if (string.IsNullOrWhiteSpace(settings.Azure.ClientSecret))
            errors.Add("Azure ClientSecret is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Uri))
            errors.Add("Neo4j Uri is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Username))
            errors.Add("Neo4j Username is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Password))
            errors.Add("Neo4j Password is not configured");

        if (errors.Count > 0)
        {
            Console.WriteLine("Configuration validation failed:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            Console.WriteLine("Please update appsettings.json or set environment variables.");
            throw new InvalidOperationException("Configuration validation failed");
        }
    }
}
