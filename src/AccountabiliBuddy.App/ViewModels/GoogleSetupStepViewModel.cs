using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row in the Google setup guide's step list.</summary>
public sealed partial class GoogleSetupStepViewModel : ObservableObject
{
    public GoogleSetupStepViewModel(GoogleSetupStep step, int number, bool isDone)
    {
        Step = step;
        Number = number;
        IsDone = isDone;
    }

    public GoogleSetupStep Step { get; }

    public int Number { get; }

    public string Label => $"{Number}. {Step.Title}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark))]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark))]
    public partial bool IsCurrent { get; set; }

    /// <summary>✓ when done, ▶ for the step shown, blank otherwise.</summary>
    public string Mark => IsCurrent ? "▶" : IsDone ? "✓" : string.Empty;
}
