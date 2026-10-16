using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One site Cold Turkey was seen blocking, with actions to copy it into a profile or forget it.</summary>
public sealed partial class SightingRowViewModel
{
    private readonly Action<SightingRowViewModel> _add;
    private readonly Action<SightingRowViewModel> _forget;

    public SightingRowViewModel(BlockedSiteSighting sighting, Action<SightingRowViewModel> add, Action<SightingRowViewModel> forget)
    {
        Site = sighting.Site;
        Details = $"Seen {sighting.TimesSeen}× · last {sighting.LastSeen.ToLocalTime():g}";
        _add = add;
        _forget = forget;
    }

    public string Site { get; }

    public string Details { get; }

    [RelayCommand]
    private void Add()
    {
        _add(this);
    }

    [RelayCommand]
    private void Forget()
    {
        _forget(this);
    }
}
