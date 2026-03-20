using System.CommandLine;
using System.Text.Json;
using Fabric_backup_lite.Core.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Fabric_backup_lite.Cli.Commands;

internal static class ListCommand
{
    public static Command Create(
        IConfiguration configuration,
        Option<string> authOption,
        Option<string?> clientIdOption,
        Option<string?> clientSecretOption,
        Option<string?> tenantIdOption)
    {
        var listCommand = new Command("list", "List Fabric resources");

        listCommand.AddCommand(CreateWorkspacesSubCommand(configuration, authOption, clientIdOption, clientSecretOption, tenantIdOption));
        listCommand.AddCommand(CreateItemsSubCommand(configuration, authOption, clientIdOption, clientSecretOption, tenantIdOption));

        return listCommand;
    }

    private static Command CreateWorkspacesSubCommand(
        IConfiguration configuration,
        Option<string> authOption,
        Option<string?> clientIdOption,
        Option<string?> clientSecretOption,
        Option<string?> tenantIdOption)
    {
        var outputOption = new Option<string>("--output", getDefaultValue: () => "table", "Output format: table or json");

        var command = new Command("workspaces", "List all accessible workspaces");
        command.AddOption(outputOption);

        command.SetHandler(async (context) =>
        {
            var auth     = context.ParseResult.GetValueForOption(authOption)!;
            var clientId = context.ParseResult.GetValueForOption(clientIdOption);
            var secret   = context.ParseResult.GetValueForOption(clientSecretOption);
            var tenant   = context.ParseResult.GetValueForOption(tenantIdOption);
            var output   = context.ParseResult.GetValueForOption(outputOption)!;
            var ct       = context.GetCancellationToken();

            await using var services  = (IAsyncDisposable)Program.BuildServices(configuration, auth, clientId, secret, tenant);
            var fabricApi = (IFabricApiClient)services.GetService(typeof(IFabricApiClient))!;

            try
            {
                var workspaces = await fabricApi.GetWorkspacesAsync(ct);

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(workspaces, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine($"{"ID",-38}  {"Name"}");
                    Console.WriteLine(new string('-', 80));
                    foreach (var ws in workspaces)
                        Console.WriteLine($"{ws.Id,-38}  {ws.Name}");
                    Console.WriteLine($"\n{workspaces.Count} workspace(s) found.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to list workspaces: {Message}", ex.Message);
                context.ExitCode = 2;
            }
        });

        return command;
    }

    private static Command CreateItemsSubCommand(
        IConfiguration configuration,
        Option<string> authOption,
        Option<string?> clientIdOption,
        Option<string?> clientSecretOption,
        Option<string?> tenantIdOption)
    {
        var workspaceOption = new Option<string>("--workspace", "Workspace name or GUID") { IsRequired = true };
        var outputOption    = new Option<string>("--output", getDefaultValue: () => "table", "Output format: table or json");

        var command = new Command("items", "List items in a workspace");
        command.AddOption(workspaceOption);
        command.AddOption(outputOption);

        command.SetHandler(async (context) =>
        {
            var auth      = context.ParseResult.GetValueForOption(authOption)!;
            var clientId  = context.ParseResult.GetValueForOption(clientIdOption);
            var secret    = context.ParseResult.GetValueForOption(clientSecretOption);
            var tenant    = context.ParseResult.GetValueForOption(tenantIdOption);
            var workspace = context.ParseResult.GetValueForOption(workspaceOption)!;
            var output    = context.ParseResult.GetValueForOption(outputOption)!;
            var ct        = context.GetCancellationToken();

            await using var services  = (IAsyncDisposable)Program.BuildServices(configuration, auth, clientId, secret, tenant);
            var fabricApi = (IFabricApiClient)services.GetService(typeof(IFabricApiClient))!;

            try
            {
                var workspaces = await fabricApi.GetWorkspacesAsync(ct);
                var resolved = workspaces.FirstOrDefault(w =>
                    w.Id.Equals(workspace, StringComparison.OrdinalIgnoreCase) ||
                    w.Name.Equals(workspace, StringComparison.OrdinalIgnoreCase));

                if (resolved is null)
                {
                    Log.Error("Workspace '{Name}' not found.", workspace);
                    context.ExitCode = 2;
                    return;
                }

                var items = await fabricApi.GetWorkspaceItemsAsync(resolved.Id, ct);

                if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine($"{"Type",-22}  {"Name"}");
                    Console.WriteLine(new string('-', 80));
                    foreach (var item in items.OrderBy(i => i.Type.ToString()).ThenBy(i => i.DisplayName))
                        Console.WriteLine($"{item.Type,-22}  {item.DisplayName}");
                    Console.WriteLine($"\n{items.Count} item(s) in workspace '{resolved.Name}'.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to list items: {Message}", ex.Message);
                context.ExitCode = 2;
            }
        });

        return command;
    }
}
