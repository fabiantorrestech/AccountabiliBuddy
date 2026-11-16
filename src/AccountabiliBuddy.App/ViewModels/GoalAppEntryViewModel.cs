using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One app or site of a screen-time source in Edit goal (ST2): its kind chip, its text, a note and a remove (×). The text is private text of
/// the name class: it stays in this dialog and the sealed goal store, and is never logged. Keeps the entry it came from, so fields a newer
/// app wrote on it travel on when nothing about it changed. ST4: it also has its own device list, chosen on its "Devices" chip among the devices
/// the source counts from (every ticked device unless a list is chosen; the ids are stored in the sealed goal and nothing else).
/// </summary>
public sealed partial class GoalAppEntryViewModel : ObservableObject
{
    private readonly GoalAppMatch? _original;
    private readonly Func<GoalAppMatch, bool> _wasSeen;
    private readonly Action<GoalAppEntryViewModel> _remove;
    private readonly Action? _changed;
    private readonly Func<IReadOnlyList<GoalDeviceOptionViewModel>>? _ticked;
    private IReadOnlyList<Guid>? _devices;
    private bool _rebuilding;

    /// <param name="ticked">The devices the source counts from now (ST4); null where devices don't apply (the chip never shows).</param>
    public GoalAppEntryViewModel(GoalAppMatch match, GoalAppMatch? original, Func<GoalAppMatch, bool> wasSeen, Action<GoalAppEntryViewModel> remove, Action changed,
        Func<IReadOnlyList<GoalDeviceOptionViewModel>>? ticked = null)
    {
        _ticked = ticked;
        _devices = match.Devices;
        RebuildChoices();
        _original = original;
        _wasSeen = wasSeen;
        _remove = remove;
        Text = match.Text;
        SelectedKind = GoalAppKindChoice.Of(match.Kind);
        _changed = changed; // set last: the first kind is not a change
    }

    public IReadOnlyList<GoalAppKindChoice> Kinds => GoalAppKindChoice.All;

    public string Text { get; }

    [ObservableProperty]
    public partial GoalAppKindChoice SelectedKind { get; set; }

    /// <summary>"Counts on PCs only · Not seen on this device yet": where the entry counts, and whether this PC's last 30 days know it.</summary>
    public string? Note
    {
        get
        {
            var match = ToMatch();
            string? where = match.Kind switch
            {
                GoalAppMatchKind.Package => "Counts on phones only",
                GoalAppMatchKind.Process or GoalAppMatchKind.Website => "Counts on PCs only",
                _ => null,
            };
            string? seen = match.Kind != GoalAppMatchKind.Package && !_wasSeen(match) ? "Not seen on this device yet" : null;
            return where is null ? seen : seen is null ? where : where + " · " + seen;
        }
    }

    public bool HasNote => Note is not null;

    /// <summary>The chip's boxes are open (they show inline under the entry, so nothing floats over the dialog).</summary>
    [ObservableProperty]
    public partial bool DevicesOpen { get; set; }

    /// <summary>The ticked devices, each with a box: where this entry counts. Rebuilt by <see cref="RefreshDevices"/>.</summary>
    public ObservableCollection<GoalEntryDeviceChoiceViewModel> DeviceChoices { get; } = [];

    /// <summary>The chip shows with two or more devices ticked, or when the entry already names devices (so a stale list can be fixed).</summary>
    public bool ShowsDevices => DeviceChoices.Count >= 2 || _devices is not null;

    /// <summary>"All ticked devices", "This PC", "SA, Pixel", "No device".</summary>
    public string DevicesText
    {
        get
        {
            if (_devices is null)
            {
                return "All ticked devices";
            }

            List<string> names = DeviceChoices.Where(choice => choice.IsChecked).Select(choice => ShortName(choice.Name)).ToList();
            int others = _devices.Count(id => !DeviceChoices.Any(choice => choice.Id == id));
            if (others > 0)
            {
                names.Add(others == 1 ? "1 other device" : $"{others} other devices");
            }

            return names.Count == 0 ? "No device" : string.Join(", ", names);
        }
    }

    /// <summary>The "All ticked devices" box of the chip: ticked while the entry has no list of its own.</summary>
    public bool AllTicked
    {
        get => _devices is null;
        set
        {
            if (value == (_devices is null))
            {
                return;
            }

            _devices = value ? null : DeviceChoices.Select(choice => choice.Id).ToList();
            Rebuilt();
        }
    }

    /// <summary>"Counts on none of the ticked devices": the entry names devices and the source counts from none of them (ST4).</summary>
    public bool CountsNowhere => _ticked is not null && GoalAppMatching.CountsNowhere(ToMatch(), DeviceChoices.Select(choice => choice.Id).ToList());

    public string NowhereText => "Counts on none of the ticked devices";

    /// <summary>Whether the entry counts on <paramref name="device"/> as far as its own list goes (no list = every device).</summary>
    public bool CountsOn(Guid device) => _devices is null || _devices.Contains(device);

    /// <summary>The source's ticked devices changed: the boxes follow them (the entry's own list stays as it is).</summary>
    public void RefreshDevices(bool notify = true)
    {
        RebuildChoices();
        Rebuilt(notify);
    }

    private void RebuildChoices()
    {
        _rebuilding = true;
        DeviceChoices.Clear();
        foreach (GoalDeviceOptionViewModel option in _ticked?.Invoke() ?? [])
        {
            DeviceChoices.Add(new GoalEntryDeviceChoiceViewModel(option.Id, ShortName(option.Name), option.Icon, CountsOn(option.Id), ChoiceChanged));
        }

        _rebuilding = false;
    }

    // A box of the chip: the checked ones are the list; all of them checked is "all ticked devices" again. Ids of devices that are not listed
    // any more (left the group, unticked on the source) stay in the list while it is one.
    private void ChoiceChanged(GoalEntryDeviceChoiceViewModel choice)
    {
        if (_rebuilding)
        {
            return;
        }

        List<Guid> picked = DeviceChoices.Where(item => item.IsChecked).Select(item => item.Id).ToList();
        _devices = picked.Count == DeviceChoices.Count
            ? null
            : picked.Concat((_devices ?? []).Where(id => !DeviceChoices.Any(item => item.Id == id))).Distinct().ToList();
        Rebuilt();
    }

    private void Rebuilt(bool notify = true)
    {
        _rebuilding = true;
        foreach (GoalEntryDeviceChoiceViewModel choice in DeviceChoices)
        {
            choice.IsChecked = CountsOn(choice.Id);
        }

        _rebuilding = false;
        OnPropertyChanged(nameof(AllTicked));
        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(ShowsDevices));
        OnPropertyChanged(nameof(CountsNowhere));
        if (notify)
        {
            _changed?.Invoke();
        }
    }

    // " (this PC)" is dropped in the chip: it is always the first device.
    private static string ShortName(string name) => name.EndsWith(" (this PC)", StringComparison.Ordinal) ? name[..^10] : name;

    /// <summary>The entry as it is set now (the original object when nothing changed, so unknown fields stay).</summary>
    public GoalAppMatch ToMatch()
    {
        GoalAppMatch match = _original is not null && _original.Kind == SelectedKind.Kind && _original.Text == Text ? _original : new GoalAppMatch(SelectedKind.Kind, Text);
        bool same = match.Devices is null ? _devices is null : _devices is not null && match.Devices.SequenceEqual(_devices);
        return same ? match : match with { Devices = _devices };
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    partial void OnSelectedKindChanged(GoalAppKindChoice value)
    {
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(CountsNowhere));
        _changed?.Invoke();
    }
}
