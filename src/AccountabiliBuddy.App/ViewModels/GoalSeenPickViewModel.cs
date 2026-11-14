using AccountabiliBuddy.App.Services.Goals;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One chip of the screen-time pick list (ST2): a program or site this PC saw in the last 30 days; a tap adds it to the source.</summary>
public sealed partial class GoalSeenPickViewModel
{
    private readonly GoalScreenTimeSeenItem _item;
    private readonly Action<GoalScreenTimeSeenItem> _add;

    public GoalSeenPickViewModel(GoalScreenTimeSeenItem item, Action<GoalScreenTimeSeenItem> add)
    {
        _item = item;
        _add = add;
    }

    public GoalScreenTimeSeenItem Item => _item;

    public string Name => _item.Name;

    public MaterialIconKind Icon => _item.IsSite ? MaterialIconKind.Web : MaterialIconKind.ApplicationOutline;

    /// <summary>The kind it adds as, and the real name when History shows another ("PC app · chrome").</summary>
    public string Tooltip => (_item.IsSite ? "Website" : "PC app") + (_item.RealName is { } real ? " · " + real : string.Empty);

    [RelayCommand]
    private void Add() => _add(_item);
}
