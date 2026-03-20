using Fabric_backup_lite.Core.Models;

namespace Fabric_backup_lite.Core.Services;

public interface IRestoreService
{
    Task<List<BackupSummary>> DiscoverBackupsAsync(
        string rootFolder,
        CancellationToken cancellationToken = default);

    Task<List<RestoreItemViewModel>> LoadManifestItemsAsync(
        string backupFolderPath,
        CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreItemsAsync(
        string backupFolderPath,
        string targetWorkspaceId,
        string targetWorkspaceName,
        IList<RestoreItemViewModel> selectedItems,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
