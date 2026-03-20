using System.CommandLine;
using Fabric_backup_lite.Core.Models;
using Fabric_backup_lite.Core.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Fabric_backup_lite.Cli.Commands;

internal static class RestoreCommand
{
    public static Command Create(
        IConfiguration configuration,
        Option<string> authOption,
        Option<string?> clientIdOption,
        Option<string?> clientSecretOption,
        Option<string?> tenantIdOption)
    {
        var sourceOption       = new Option<string>("--source", "Path to backup folder (containing manifest.json)") { IsRequired = true };
        var workspaceOption    = new Option<string?>("--workspace", "Target workspace name or GUID");
        var newWorkspaceOption = new Option<string?>("--new-workspace", "Name for a new workspace to create");
        var capacityOption     = new Option<string?>("--capacity", "Fabric capacity ID (required with --new-workspace)");
        var itemTypesOption    = new Option<string?>("--item-types", "Comma-separated item types to restore");

        var command = new Command("restore", "Restore Fabric items from a local backup folder");
        command.AddOption(sourceOption);
        command.AddOption(workspaceOption);
        command.AddOption(newWorkspaceOption);
        command.AddOption(capacityOption);
        command.AddOption(itemTypesOption);

        command.SetHandler(async (context) =>
        {
            var auth           = context.ParseResult.GetValueForOption(authOption)!;
            var clientId       = context.ParseResult.GetValueForOption(clientIdOption);
            var secret         = context.ParseResult.GetValueForOption(clientSecretOption);
            var tenant         = context.ParseResult.GetValueForOption(tenantIdOption);
            var source         = context.ParseResult.GetValueForOption(sourceOption)!;
            var workspace      = context.ParseResult.GetValueForOption(workspaceOption);
            var newWorkspace   = context.ParseResult.GetValueForOption(newWorkspaceOption);
            var capacity       = context.ParseResult.GetValueForOption(capacityOption);
            var itemTypes      = context.ParseResult.GetValueForOption(itemTypesOption);
            var ct             = context.GetCancellationToken();

            if (string.IsNullOrEmpty(workspace) && string.IsNullOrEmpty(newWorkspace))
            {
                Log.Error("Specify --workspace <name-or-id> or --new-workspace <name>.");
                context.ExitCode = 2;
                return;
            }
            if (!string.IsNullOrEmpty(workspace) && !string.IsNullOrEmpty(newWorkspace))
            {
                Log.Error("Use either --workspace <name-or-id> or --new-workspace <name>, not both.");
                context.ExitCode = 2;
                return;
            }

            if (!string.IsNullOrEmpty(newWorkspace) && string.IsNullOrEmpty(capacity))
            {
                Log.Error("--new-workspace requires --capacity <id>.");
                context.ExitCode = 2;
                return;
            }
            if (!string.IsNullOrEmpty(workspace) && !string.IsNullOrEmpty(capacity))
            {
                Log.Error("--capacity is only valid with --new-workspace.");
                context.ExitCode = 2;
                return;
            }

            await using var services = (IAsyncDisposable)Program.BuildServices(configuration, auth, clientId, secret, tenant);
            var fabricApi   = (IFabricApiClient)services.GetService(typeof(IFabricApiClient))!;
            var restoreSvc  = (IRestoreService)services.GetService(typeof(IRestoreService))!;

            try
            {
                string targetId, targetName;

                if (!string.IsNullOrEmpty(newWorkspace))
                {
                    Log.Information("Creating workspace '{Name}'...", newWorkspace);
                    var created = await fabricApi.CreateWorkspaceAsync(newWorkspace, capacity!, ct);
                    targetId   = created.Id;
                    targetName = created.Name;
                    Log.Information("Created workspace '{Name}' ({Id})", targetName, targetId);
                }
                else
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
                    targetId   = resolved.Id;
                    targetName = resolved.Name;
                }

                var manifestItems = await restoreSvc.LoadManifestItemsAsync(source, ct);

                var typeFilter = ParseItemTypes(itemTypes);
                var toRestore = typeFilter.Count > 0
                    ? manifestItems.Where(i => i.IsRestorable && typeFilter.Contains(i.ItemType)).ToList()
                    : manifestItems.Where(i => i.IsRestorable).ToList();

                var skipped = manifestItems.Where(i => !i.IsRestorable).ToList();
                if (skipped.Count > 0)
                    Log.Warning("Skipping {Count} non-restorable item(s) (Warehouse).", skipped.Count);

                Log.Information("Restoring {Count} item(s) to workspace '{Name}'...", toRestore.Count, targetName);

                var progress = new Progress<RestoreProgress>(p =>
                    Log.Information("[{Done}/{Total}] {Item} — {Msg}", p.CompletedItems, p.TotalItems, p.CurrentItem, p.Message));

                var result = await restoreSvc.RestoreItemsAsync(source, targetId, targetName, toRestore, progress, ct);

                foreach (var err in result.Errors)
                    Log.Warning("  {Error}", err);

                Log.Information("Restore complete: {Count} item(s) restored.", result.ItemsRestored);
                context.ExitCode = result.Errors.Count > 0 ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Restore cancelled.");
                context.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Restore failed: {Message}", ex.Message);
                context.ExitCode = 2;
            }
        });

        return command;
    }

    private static HashSet<FabricItemType> ParseItemTypes(string? itemTypes)
    {
        if (string.IsNullOrEmpty(itemTypes)) return new HashSet<FabricItemType>();
        return itemTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Enum.TryParse<FabricItemType>(t.Trim(), true, out var parsed) ? parsed : FabricItemType.Unknown)
            .Where(t => t != FabricItemType.Unknown)
            .ToHashSet();
    }
}
