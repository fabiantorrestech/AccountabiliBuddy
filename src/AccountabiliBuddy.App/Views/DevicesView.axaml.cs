using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Devices tab; its file and folder pickers.</summary>
public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
    }

    // Sync files (.absync) or pictures of the phone's QR codes.
    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel devices || TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a sync file or pictures of the phone's codes",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Sync files and pictures") { Patterns = ["*.absync", "*.png", "*.jpg", "*.jpeg"] },
                FilePickerFileTypes.All,
            ],
        });
        List<string> paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        await devices.ImportAsync(paths);
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel devices || TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the sync folder (e.g. inside Google Drive)",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path)
        {
            await devices.SetFolderAsync(path);
        }
    }
}
