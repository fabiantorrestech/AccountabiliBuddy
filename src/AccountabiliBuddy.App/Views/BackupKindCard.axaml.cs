using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

/// <summary>One kind of backup on Settings › Backup (ADR 71); supplies the file pickers for its extension.</summary>
public partial class BackupKindCard : UserControl
{
    public BackupKindCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BackupKindCardViewModel viewModel)
            {
                viewModel.PickOpenFile = () => PickOpenFileAsync(viewModel);
                viewModel.PickSaveFile = name => PickSaveFileAsync(viewModel, name);
            }
        };
    }

    private async Task<string?> PickOpenFileAsync(BackupKindCardViewModel viewModel)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose a {viewModel.Title.ToLowerInvariant()} backup",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(viewModel.Title) { Patterns = ["*" + viewModel.Extension] }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveFileAsync(BackupKindCardViewModel viewModel, string suggestedName)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return null;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {viewModel.Title.ToLowerInvariant()}",
            SuggestedFileName = suggestedName,
            DefaultExtension = viewModel.Extension,
            FileTypeChoices = [new FilePickerFileType(viewModel.Title) { Patterns = ["*" + viewModel.Extension] }],
        });
        return file?.TryGetLocalPath();
    }
}
