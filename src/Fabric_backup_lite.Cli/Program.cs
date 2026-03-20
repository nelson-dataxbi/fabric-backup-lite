using System.CommandLine;
using Fabric_backup_lite.Cli.Commands;
using Fabric_backup_lite.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Fabric_backup_lite.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/fbl-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var authOption = new Option<string>(
                "--auth",
                getDefaultValue: () => Environment.GetEnvironmentVariable("FABRIC_AUTH") ?? "az-login",
                description: "Authentication mode: az-login or sp");

            var clientIdOption = new Option<string?>(
                "--client-id",
                getDefaultValue: () => Environment.GetEnvironmentVariable("FABRIC_CLIENT_ID"),
                description: "Service principal client ID");

            var clientSecretOption = new Option<string?>(
                "--client-secret",
                getDefaultValue: () => Environment.GetEnvironmentVariable("FABRIC_CLIENT_SECRET"),
                description: "Service principal client secret");

            var tenantIdOption = new Option<string?>(
                "--tenant-id",
                getDefaultValue: () => Environment.GetEnvironmentVariable("FABRIC_TENANT_ID"),
                description: "Service principal tenant ID");

            var rootCommand = new RootCommand("Fabric Backup Lite CLI — backup and restore Microsoft Fabric workspaces");
            rootCommand.AddGlobalOption(authOption);
            rootCommand.AddGlobalOption(clientIdOption);
            rootCommand.AddGlobalOption(clientSecretOption);
            rootCommand.AddGlobalOption(tenantIdOption);

            rootCommand.AddCommand(BackupCommand.Create(configuration, authOption, clientIdOption, clientSecretOption, tenantIdOption));
            rootCommand.AddCommand(RestoreCommand.Create(configuration, authOption, clientIdOption, clientSecretOption, tenantIdOption));
            rootCommand.AddCommand(ListCommand.Create(configuration, authOption, clientIdOption, clientSecretOption, tenantIdOption));

            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error");
            return 2;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    public static IServiceProvider BuildServices(
        IConfiguration configuration,
        string authMode,
        string? clientId,
        string? clientSecret,
        string? tenantId)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddSingleton(configuration);
        services.AddSingleton<FileSystemService>();
        services.AddSingleton<IFabCliService, FabCliService>();
        services.AddSingleton<IOneLakeDownloadService, OneLakeDownloadService>();
        services.AddSingleton<IFabricApiClient, FabricApiClient>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestoreService, RestoreService>();

        if (authMode.Equals("sp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
                throw new InvalidOperationException("--auth sp requires --client-id, --client-secret, and --tenant-id (or FABRIC_CLIENT_ID/SECRET/TENANT_ID env vars).");

            services.AddSingleton<IAuthenticationService>(sp =>
                new ServicePrincipalAuthService(
                    clientId!, clientSecret!, tenantId!,
                    sp.GetRequiredService<ILogger<ServicePrincipalAuthService>>()));
        }
        else
        {
            services.AddSingleton<IAuthenticationService, AzureCliAuthService>();
        }

        return services.BuildServiceProvider();
    }
}
