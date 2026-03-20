using System.CommandLine;
using Fabric_backup_lite.Core.Models;
using Fabric_backup_lite.Core.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Fabric_backup_lite.Cli.Commands;

internal static class BackupCommand
{
    public static Command Create(
        IConfiguration configuration,
        Option<string> authOption,
        Option<string?> clientIdOption,
        Option<string?> clientSecretOption,
        Option<string?> tenantIdOption)
    {
        var workspaceOption = new Option<string?>("--workspace", "Workspace name or GUID");
        var allOption = new Option<bool>("--all", "Backup all accessible workspaces");
        var destOption = new Option<string>("--dest", "Destination folder path") { IsRequired = true };
        var itemTypesOption = new Option<string?>("--item-types", "Comma-separated item types to include");

        var command = new Command("backup", "Backup Fabric workspace(s) to a local folder");
        command.AddOption(workspaceOption);
        command.AddOption(allOption);
        command.AddOption(destOption);
        command.AddOption(itemTypesOption);

        command.SetHandler(async (context) =>
        {
            var auth        = context.ParseResult.GetValueForOption(authOption)!;
            var clientId    = context.ParseResult.GetValueForOption(clientIdOption);
            var secret      = context.ParseResult.GetValueForOption(clientSecretOption);
            var tenant      = context.ParseResult.GetValueForOption(tenantIdOption);
            var workspace   = context.ParseResult.GetValueForOption(workspaceOption);
            var all         = context.ParseResult.GetValueForOption(allOption);
            var dest        = context.ParseResult.GetValueForOption(destOption)!;
            var itemTypes   = context.ParseResult.GetValueForOption(itemTypesOption);
            var ct          = context.GetCancellationToken();

            if (!all && string.IsNullOrEmpty(workspace))
            {
                Log.Error("Specify --workspace <name-or-id> or --all.");
                context.ExitCode = 2;
                return;
            }
            if (all && !string.IsNullOrEmpty(workspace))
            {
                Log.Error("Use either --workspace <name-or-id> or --all, not both.");
                context.ExitCode = 2;
                return;
            }

            await using var services = (IAsyncDisposable)Program.BuildServices(configuration, auth, clientId, secret, tenant);
            var fabricApi   = (IFabricApiClient)services.GetService(typeof(IFabricApiClient))!;
            var backupSvc   = (IBackupService)services.GetService(typeof(IBackupService))!;

            var typeFilter = ParseItemTypes(itemTypes);

            try
            {
                var workspaces = await fabricApi.GetWorkspacesAsync(ct);
                List<string> workspaceIds;

                if (all)
                {
                    workspaceIds = workspaces.Select(w => w.Id).ToList();
                    Log.Information("Backing up {Count} workspaces...", workspaceIds.Count);
                }
                else
                {
                    var resolved = ResolveWorkspace(workspace!, workspaces);
                    if (resolved is null)
                    {
                        context.ExitCode = 2;
                        return;
                    }
                    workspaceIds = [resolved.Id];
                }

                int totalSucceeded = 0, totalFailed = 0;

                foreach (var wsId in workspaceIds)
                {
                    var ws = workspaces.First(w => w.Id == wsId);
                    Log.Information("Backing up workspace '{Name}' ({Id})...", ws.Name, ws.Id);

                    BackupResult result;

                    if (typeFilter.Count > 0)
                    {
                        var items = await fabricApi.GetWorkspaceItemsAsync(wsId, ct);
                        var filtered = items
                            .Where(i => typeFilter.Contains(i.Type))
                            .Select(i => (wsId, ws.Name, i))
                            .ToList();

                        var progress = new Progress<Fabric_backup_lite.Core.Models.BackupProgress>(p =>
                            Log.Information("[{Done}/{Total}] {Item} — {Msg}", p.CompletedItems, p.TotalItems, p.CurrentItem, p.Message));

                        result = await backupSvc.BackupSelectedItemsAsync(filtered, dest, progress, ct);
                    }
                    else
                    {
                        var progress = new Progress<Fabric_backup_lite.Core.Models.BackupProgress>(p =>
                            Log.Information("[{Done}/{Total}] {Item} — {Msg}", p.CompletedItems, p.TotalItems, p.CurrentItem, p.Message));

                        result = await backupSvc.BackupWorkspaceAsync(wsId, dest, progress, ct);
                    }

                    totalSucceeded += result.ItemsBackedUp;
                    totalFailed += result.Errors.Count;

                    foreach (var err in result.Errors)
                        Log.Warning("  {Error}", err);
                }

                Log.Information("Backup complete: {Succeeded} succeeded, {Failed} failed.", totalSucceeded, totalFailed);
                context.ExitCode = totalFailed > 0 ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Backup cancelled.");
                context.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Backup failed: {Message}", ex.Message);
                context.ExitCode = 2;
            }
        });

        return command;
    }

    private static Workspace? ResolveWorkspace(string nameOrId, List<Workspace> workspaces)
    {
        var byId = workspaces.FirstOrDefault(w =>
            w.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;

        var byName = workspaces
            .Where(w => w.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1) return byName[0];

        if (byName.Count > 1)
        {
            Log.Error("Workspace name '{Name}' matches {Count} workspaces. Use the GUID instead:", nameOrId, byName.Count);
            foreach (var ws in byName)
                Log.Error("  {Id}  {Name}", ws.Id, ws.Name);
            return null;
        }

        Log.Error("Workspace '{Name}' not found.", nameOrId);
        return null;
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
