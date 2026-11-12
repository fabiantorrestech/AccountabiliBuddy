using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Backup;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Tasks view chooser (<see cref="TaskViewChooserViewModel"/>): picks the backup file for it.</summary>
public partial class TaskViewChooserView : UserControl
{
    private static readonly FilePickerFileType BackupFiles = new("AccountabiliBuddy backup")
    {
        Patterns = ["*" + BackupEnvelope.FileExtension],
    };

    public TaskViewChooserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TaskViewChooserViewModel viewModel)
            {
                viewModel.PickBackupFile = PickBackupFileAsync;
            }
        };
    }

    // A backup made on this PC: by default the backups folder in Documents.
    private async Task<string?> PickBackupFileAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IStorageFolder? start = Directory.Exists(Services.Backup.AutoBackupService.DefaultFolder)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(Services.Backup.AutoBackupService.DefaultFolder)
            : null;
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a backup from this PC",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [BackupFiles, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
