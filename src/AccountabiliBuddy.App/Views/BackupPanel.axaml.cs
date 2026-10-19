using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Backup;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

public partial class BackupPanel : UserControl
{
    private static readonly FilePickerFileType BackupFiles = new("AccountabiliBuddy backup")
    {
        Patterns = new[] { "*" + BackupEnvelope.FileExtension },
    };

    public BackupPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BackupViewModel viewModel)
            {
                viewModel.PickOpenFile = PickOpenFileAsync;
                viewModel.PickSaveFile = PickSaveFileAsync;
            }
        };
    }

    private async Task<string?> PickOpenFileAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an AccountabiliBuddy backup",
            AllowMultiple = false,
            FileTypeFilter = new[] { BackupFiles, FilePickerFileTypes.All },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save backup",
            SuggestedFileName = suggestedName,
            DefaultExtension = BackupEnvelope.FileExtension.TrimStart('.'),
            FileTypeChoices = new[] { BackupFiles },
        });
        return file?.TryGetLocalPath();
    }
}
