using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

/// <summary>The "Remove this device" dialog (ADR 54, 55); its save-file picker for the backup.</summary>
public partial class RemoveDeviceDialog : Window
{
    private static readonly FilePickerFileType SyncFiles = new("AccountabiliBuddy sync file")
    {
        Patterns = ["*.absync"],
    };

    public RemoveDeviceDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RemoveDeviceViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
                viewModel.PickBackupFile = PickBackupFileAsync;
            }
        };
    }

    private async Task<string?> PickBackupFileAsync(string folder, string suggestedName)
    {
        // The default folder is made first so the picker can open in it.
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The picker opens somewhere else.
        }

        IStorageFolder? start = await StorageProvider.TryGetFolderFromPathAsync(folder);
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the backup",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = start,
            DefaultExtension = "absync",
            FileTypeChoices = [SyncFiles],
        });
        return file?.TryGetLocalPath();
    }
}
