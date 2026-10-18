using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Editable copy of one <see cref="UnlockOption"/> in the Profiles tab.</summary>
public sealed partial class UnlockOptionEditorViewModel : ObservableObject
{
    private const int MaxWaitMinutes = 12 * 60;

    private readonly Guid _id;
    private readonly Action<UnlockOptionEditorViewModel> _remove;

    public UnlockOptionEditorViewModel(UnlockOption option, Action<UnlockOptionEditorViewModel> remove)
    {
        _id = option.Id;
        _remove = remove;
        Name = option.Name;
        WaitMinutes = ((int)option.Wait.TotalMinutes).ToString();
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string WaitMinutes { get; set; }

    /// <returns>An error message, or null when valid.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "every unlock option needs a name";
        }

        return int.TryParse(WaitMinutes.Trim(), out int minutes) && minutes is >= 0 and <= MaxWaitMinutes
            ? null
            : $"unlock option '{Name}': wait must be 0–{MaxWaitMinutes} minutes";
    }

    public UnlockOption ToOption()
    {
        return new UnlockOption(_id, Name.Trim(), TimeSpan.FromMinutes(int.Parse(WaitMinutes.Trim())));
    }

    [RelayCommand]
    private void Remove()
    {
        _remove(this);
    }
}
