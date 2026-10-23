using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One profile in the hyperfocus card: ticked when its sessions get the reminder.</summary>
public sealed partial class HyperfocusProfileRowViewModel(Guid id, string name, bool reminds) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool Reminds { get; set; } = reminds;
}
