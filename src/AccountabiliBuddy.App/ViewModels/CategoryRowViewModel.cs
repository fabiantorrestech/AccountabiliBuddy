using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One category in the Day plan tab: name, colour and what it asks of the buddy (Watch, Rest, Sleep).</summary>
public sealed partial class CategoryRowViewModel : ObservableObject
{
    private readonly Action<CategoryRowViewModel> _delete;

    public CategoryRowViewModel(DayCategory category, Action<CategoryRowViewModel> delete)
    {
        Id = category.Id;
        Name = category.Name;
        Behavior = category.Behavior;
        Colors = ColorChoice.Palette.Any(choice => string.Equals(choice.Hex, category.Color, StringComparison.OrdinalIgnoreCase))
            ? ColorChoice.Palette
            : ColorChoice.Palette.Append(new ColorChoice(category.Color)).ToList();
        SelectedColor = Colors.First(choice => string.Equals(choice.Hex, category.Color, StringComparison.OrdinalIgnoreCase));
        _delete = delete;
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>The palette, plus this category's own colour when it isn't one of them.</summary>
    public IReadOnlyList<ColorChoice> Colors { get; }

    [ObservableProperty]
    public partial ColorChoice SelectedColor { get; set; }

    [ObservableProperty]
    public partial CategoryBehavior Behavior { get; set; }

    public static IReadOnlyList<CategoryBehavior> Behaviors { get; } = Enum.GetValues<CategoryBehavior>();

    public DayCategory ToCategory()
    {
        return new DayCategory(Id, Name.Trim(), SelectedColor.Hex, Behavior);
    }

    [RelayCommand]
    private void Delete()
    {
        _delete(this);
    }
}
