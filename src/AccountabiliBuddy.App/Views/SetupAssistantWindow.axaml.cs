using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

public partial class SetupAssistantWindow : Window
{
    private static readonly FilePickerFileType LeechBlockFiles = new("LeechBlock options")
    {
        Patterns = new[] { "*.txt", "*.json" },
    };

    public SetupAssistantWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SetupAssistantViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
                viewModel.PickFile = PickFileAsync;
                viewModel.PickFolder = PickFolderAsync;
            }
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the sync folder (e.g. inside Google Drive)",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a LeechBlock options file",
            AllowMultiple = false,
            FileTypeFilter = new[] { LeechBlockFiles, FilePickerFileTypes.All },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
