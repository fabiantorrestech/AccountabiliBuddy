using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

public partial class SettingsView : UserControl
{
    private static readonly FilePickerFileType WaveFiles = new("Sound (.wav)") { Patterns = new[] { "*.wav" } };

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.PickSoundFile = PickSoundFileAsync;
            }
        };
    }

    private async Task<string?> PickSoundFileAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a sound",
            AllowMultiple = false,
            FileTypeFilter = new[] { WaveFiles },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
