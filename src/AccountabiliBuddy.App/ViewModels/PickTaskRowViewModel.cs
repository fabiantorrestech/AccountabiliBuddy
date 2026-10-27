using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One task in the "Pick 3 for this session" prompt: its title and whether it is kept.</summary>
public sealed partial class PickTaskRowViewModel : ObservableObject
{
    public PickTaskRowViewModel(Guid id, string title, bool isPicked)
    {
        Id = id;
        Title = title;
        IsPicked = isPicked;
    }

    public Guid Id { get; }

    public string Title { get; }

    [ObservableProperty]
    public partial bool IsPicked { get; set; }
}
