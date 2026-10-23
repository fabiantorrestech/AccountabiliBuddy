using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One open come-back: its text and age, and ticking it.</summary>
public sealed partial class ComebackRowViewModel(Comeback comeback, Action<Guid> tick) : ObservableObject
{
    public string Text { get; } = comeback.Text;

    /// <summary>E.g. "Thu 10:42 AM".</summary>
    public string When { get; } = $"{comeback.CreatedAt.ToLocalTime():ddd h:mm tt}";

    [RelayCommand]
    private void Tick()
    {
        tick(comeback.Id);
    }
}
