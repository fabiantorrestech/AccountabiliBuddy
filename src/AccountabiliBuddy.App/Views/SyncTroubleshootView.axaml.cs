using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AccountabiliBuddy.App.Views;

/// <summary>Troubleshoot sync (SYNC1b): the confirmation board, the lists and the buttons; its Save as dialog and the conflict dialog.</summary>
public partial class SyncTroubleshootView : UserControl
{
    private SyncTroubleshootViewModel? _subscribed;

    public SyncTroubleshootView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.ConflictRequested -= OnConflictRequested;
            _subscribed.ConfirmRequested -= OnConfirmRequested;
        }

        _subscribed = DataContext as SyncTroubleshootViewModel;
        if (_subscribed is not null)
        {
            _subscribed.ConflictRequested += OnConflictRequested;
            _subscribed.ConfirmRequested += OnConfirmRequested;
        }
    }

    // "Remove my stale file" asks first (Remove / Cancel).
    private void OnConfirmRequested(object? sender, GoalConfirmRequest request)
    {
        _ = GoalConfirmDialog.ShowAsync(this, request);
    }

    private void OnConflictRequested(object? sender, ConflictDialogViewModel dialog)
    {
        _ = ConflictDialog.ShowAsync(this, dialog);
    }

    // The report is saved where the user picks (Documents first); a place inside the sync folder is refused by the service.
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SyncTroubleshootViewModel troubleshoot || TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the sync diagnostics report",
            SuggestedFileName = troubleshoot.SuggestedReportName,
            DefaultExtension = "txt",
            SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
            FileTypeChoices = [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }],
        });
        if (file?.TryGetLocalPath() is string path)
        {
            await troubleshoot.ExportToAsync(path);
        }
    }
}
