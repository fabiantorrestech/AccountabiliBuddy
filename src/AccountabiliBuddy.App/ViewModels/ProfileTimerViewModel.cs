using System.Diagnostics.CodeAnalysis;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One profile's on-screen timer choice in the Settings tab (the same one as in the profile's editor).</summary>
public sealed partial class ProfileTimerViewModel : ObservableObject
{
    public ProfileTimerViewModel(Guid id, string name, ProfileOverlay overlay)
    {
        Id = id;
        Name = name;
        TimerColors = TimerColorOption.For(id);
        ShowOverlay(overlay);
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>A <see cref="TimerChoice"/> index: use the default, on or off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnTimer))]
    public partial int TimerChoiceIndex { get; set; }

    /// <summary>True when the timer is on by the profile's own choice, so it has its own colour.</summary>
    public bool HasOwnTimer => TimerChoiceIndex == TimerChoice.On;

    [ObservableProperty]
    public partial TimerColorOption TimerColor { get; set; }

    public IReadOnlyList<TimerColorOption> TimerColors { get; }

    public ProfileOverlay ToOverlay()
    {
        return TimerChoice.ToOverlay(TimerChoiceIndex, TimerColor.Hex);
    }

    [MemberNotNull(nameof(TimerColor))]
    public void ShowOverlay(ProfileOverlay overlay)
    {
        TimerChoiceIndex = TimerChoice.IndexOf(overlay);
        TimerColor = TimerColorOption.Match(TimerColors, overlay.Color);
    }
}
