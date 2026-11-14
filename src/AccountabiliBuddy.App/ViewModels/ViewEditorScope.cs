using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the Create / Edit view dialog needs to know about labels and tags kept to one device (ADR 89).</summary>
/// <param name="CanScope">This device is in a sync group: the lists show "Shared" and "This device" headings.</param>
/// <param name="IsSharedLabel">Whether a label shown here is shared (else it lists under "This device").</param>
/// <param name="IsSharedTag">Whether a tag shown here is shared.</param>
/// <param name="HiddenLabelIds">The labels this device doesn't show: a view that names one keeps it when saved.</param>
/// <param name="TagShown">Whether this device shows a tag; a hidden tag the view names is kept when saved.</param>
/// <param name="HiddenNote">"Uses a label from Laptop" when the view names a label or tag this device doesn't show; null for none.</param>
public sealed record ViewEditorScope(
    bool CanScope,
    Func<TaskLabel, bool> IsSharedLabel,
    Func<string, bool> IsSharedTag,
    IReadOnlyCollection<Guid> HiddenLabelIds,
    Func<string, bool> TagShown,
    string? HiddenNote);
