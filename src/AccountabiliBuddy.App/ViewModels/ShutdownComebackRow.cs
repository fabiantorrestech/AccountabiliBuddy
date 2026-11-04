using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>An open come-back on the shutdown routine's first step: it gets a home (a task, Later with a date) or is ticked off.</summary>
public sealed partial class ShutdownComebackRow(Guid id, string text) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Text { get; } = text;

    /// <summary>The choice box's index (<see cref="ShutdownComebackChoice"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Choice), nameof(AsksForDate))]
    public partial int ChoiceIndex { get; set; }

    /// <summary>The due date of the Later item, when one is picked.</summary>
    [ObservableProperty]
    public partial DateTime? DueDate { get; set; }

    public ShutdownComebackChoice Choice => Enum.IsDefined((ShutdownComebackChoice)ChoiceIndex) ? (ShutdownComebackChoice)ChoiceIndex : ShutdownComebackChoice.Keep;

    /// <summary>The date box shows only for a thought that goes to Later.</summary>
    public bool AsksForDate => Choice == ShutdownComebackChoice.Later;

    /// <summary>The words of the choice box.</summary>
    public static IReadOnlyList<string> ChoiceNames { get; } = ["Keep it", "Done", "Make it a task", "Later"];
}
