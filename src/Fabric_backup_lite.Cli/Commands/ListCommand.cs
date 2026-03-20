using System.CommandLine;
using System.Text.Json;
using Fabric_backup_lite.Core.Models;
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
        listCommand.AddCommand(CreateBackupsSubCommand());

        return listCommand;
    }

    private static Command CreateBackupsSubCommand()
    {
        var rootOption   = new Option<string>("--root", "Root folder to search for backups") { IsRequired = true };
        var outputOption = new Option<string>("--output", getDefaultValue: () => "table", "Output format: table or json");

        var command = new Command("backups", "List available backups under a root folder");
        command.AddOption(rootOption);
        command.AddOption(outputOption);

        command.SetHandler(async (context) =>
        {
            var root   = context.ParseResult.GetValueForOption(rootOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption)!;
            var ct     = context.GetCancellationToken();

            if (!Directory.Exists(root))
            {
                Log.Error("Folder not found: {Root}", root);
                context.ExitCode = 2;
                return;
            }

            var opts          = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifestPaths = Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories);
            var summaries     = new List<(BackupMetadata Meta, string Path)>();

            foreach (var manifestPath in manifestPaths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json     = await File.ReadAllTextAsync(manifestPath, ct);
                    var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, opts);
                    if (metadata is null) continue;
                    summaries.Add((metadata, Path.GetDirectoryName(manifestPath)!));
                }
                catch (Exception ex)
                {
                    Log.Warning("Could not parse manifest at {Path}: {Msg}", manifestPath, ex.Message);
                }
            }

            summaries.Sort((a, b) => b.Meta.Timestamp.CompareTo(a.Meta.Timestamp));

            if (output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var result = summaries.Select((s, i) => new
                {
                    index     = i + 1,
                    workspace = s.Meta.WorkspaceName,
                    date      = s.Meta.Timestamp,
                    items     = s.Meta.Items.Count,
                    path      = s.Path
                });
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"{"#",-3}  {"Workspace",-30}  {"Date",-19}  {"Items",5}");
                Console.WriteLine(new string('-', 68));
                for (int i = 0; i < summaries.Count; i++)
                {
                    var s = summaries[i];
                    Console.WriteLine($"{i + 1,-3}  {s.Meta.WorkspaceName,-30}  {s.Meta.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),-19}  {s.Meta.Items.Count,5}");
                }
                Console.WriteLine();
                Console.WriteLine($"{summaries.Count} backup(s) found.");
            }
        });

        return command;
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

            var services  = Program.BuildServices(configuration, auth, clientId, secret, tenant);
            await using var _disposal = (IAsyncDisposable)services;
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

            var services  = Program.BuildServices(configuration, auth, clientId, secret, tenant);
            await using var _disposal = (IAsyncDisposable)services;
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
