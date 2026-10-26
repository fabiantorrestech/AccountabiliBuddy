using AccountabiliBuddy.App.Services.Search;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

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
                viewModel.PickNoiseFolder = PickNoiseFolderAsync;
            }
        };
    }

    /// <summary>Shows the group and brings the named section's card into view (once the group has been laid out).</summary>
    public void ShowSection(int groupIndex, string sectionName)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedGroupIndex = groupIndex;
        Dispatcher.UIThread.Post(() =>
        {
            Control? card = SectionsScroller.GetLogicalDescendants().OfType<Control>()
                .FirstOrDefault(control => control.IsEffectivelyVisible && SearchArea.GetName(control) == sectionName && control is Border);
            card?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    // Another group: start at its top (one scroll viewer holds all the groups, so its offset would carry over).
    private void OnGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        SectionsScroller.Offset = default;
    }

    // A section name under a group in the side list: show that group and scroll to the section.
    private void OnSectionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: string sectionName } link
            || link.FindLogicalAncestorOfType<ListBoxItem>()?.DataContext is not SettingsGroupViewModel group
            || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        ShowSection(viewModel.Groups.ToList().IndexOf(group), sectionName);
        e.Handled = true;
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

    private async Task<string?> PickNoiseFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Folder of noise sounds", AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
