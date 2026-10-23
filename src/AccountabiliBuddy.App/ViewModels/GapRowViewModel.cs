using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One distracted stretch in History's "Distracted time" form: from and to (times of the session's day).</summary>
public sealed partial class GapRowViewModel : ObservableObject
{
    private readonly Action<GapRowViewModel> _remove;

    public GapRowViewModel(TimeSpan from, TimeSpan to, Action<GapRowViewModel> remove)
    {
        From = from;
        To = to;
        _remove = remove;
    }

    [ObservableProperty]
    public partial TimeSpan? From { get; set; }

    [ObservableProperty]
    public partial TimeSpan? To { get; set; }

    [RelayCommand]
    private void Remove() => _remove(this);
}
