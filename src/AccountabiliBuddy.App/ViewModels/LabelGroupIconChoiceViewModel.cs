using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One icon in a category's icon picker on the Labels screen (ADR 93, ADR 32): a neutral key shared with the phone
/// (<see cref="Core.Domain.TaskLabelGroupIcons"/>), shown with this PC's Material icon (<see cref="LabelGroupIconKinds"/>).
/// </summary>
public sealed partial class LabelGroupIconChoiceViewModel : ObservableObject
{
    private readonly Action<string> _pick;

    public LabelGroupIconChoiceViewModel(string key, bool isPicked, Action<string> pick)
    {
        Key = key;
        Icon = LabelGroupIconKinds.Of(key);
        Name = key.Length == 0 ? key : char.ToUpper(key[0], CultureInfo.InvariantCulture) + key[1..];
        IsPicked = isPicked;
        _pick = pick;
    }

    /// <summary>The stored key ("work", "home"…).</summary>
    public string Key { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>The key as a word for the tooltip and the screen reader ("Work").</summary>
    public string Name { get; }

    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    [RelayCommand]
    private void Pick() => _pick(Key);

    /// <summary>Every icon a category can have, in the picker's order, with <paramref name="picked"/> marked.</summary>
    public static IReadOnlyList<LabelGroupIconChoiceViewModel> All(string picked, Action<string> pick)
    {
        return Core.Domain.TaskLabelGroupIcons.All
            .Select(key => new LabelGroupIconChoiceViewModel(key, string.Equals(key, picked, StringComparison.Ordinal), pick))
            .ToList();
    }
}
