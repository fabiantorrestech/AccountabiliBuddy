using AccountabiliBuddy.App.Services.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Devices › For testing: the sync receipts switch (<see cref="SyncReceiptsService"/>), off by default.</summary>
public sealed partial class SyncReceiptsViewModel : ObservableObject
{
    private readonly SyncReceiptsService _receipts;

    public SyncReceiptsViewModel(SyncReceiptsService receipts)
    {
        _receipts = receipts;
        IsOn = receipts.IsOn;
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (value != _receipts.IsOn)
        {
            _ = _receipts.SetOnAsync(value);
        }
    }
}
