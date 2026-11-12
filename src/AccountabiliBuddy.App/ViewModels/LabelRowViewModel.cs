using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One label on the Labels screen (spec 03 §C, 05 §16, prototype's label rows): its colour dot (the colour flyout), its name (renamed
/// in place: Enter or clicking away keeps it, Esc puts it back; a blank or taken name shows red and isn't saved), "3 tasks", the
/// Rings on ▾ device checklist and delete with confirm ("Delete · 3 tasks become No label" / Keep). The row stays while its label
/// does and takes changes in place (<see cref="Update"/>), so an open flyout or a name being typed isn't lost.
/// </summary>
public sealed partial class LabelRowViewModel : ObservableObject
{
    private readonly TaskLabelService _labels;
    private readonly Action<string> _say;
    private IReadOnlyList<TaskRingDevice> _devices;
    private int _uses;
    private string _savedName;

    public LabelRowViewModel(TaskLabel label, int uses, IReadOnlyList<TaskRingDevice> devices, TaskLabelService labels, Action<string> say)
    {
        _labels = labels;
        _say = say;
        _devices = devices;
        Id = label.Id;
        _savedName = label.Name;
        Name = label.Name;
        Color = label.Color;
        ColorBrush = Brushes.Transparent;
        CountText = RingText = DeleteText = string.Empty;
        RingChoices = [];
        ColorEditor = new LabelColorEditorViewModel(label.Color, color => _ = _labels.RecolourAsync(Id, color));
        Update(label, uses, devices);
    }

    public Guid Id { get; }

    /// <summary>The label's colour as stored.</summary>
    public string Color { get; private set; }

    public IReadOnlyList<Guid>? RingOn { get; private set; }

    [ObservableProperty]
    public partial IBrush ColorBrush { get; set; }

    public LabelColorEditorViewModel ColorEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameBad))]
    public partial string Name { get; set; }

    /// <summary>The name being typed is blank or another label's: the box shows red and the name isn't kept.</summary>
    public bool IsNameBad => _labels.CheckName(Id, Name) != TaskLabelNameCheck.Fine;

    /// <summary>"No tasks", "1 task", "3 tasks".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>"All devices", "Work PC only", "Work PC, Pixel 8".</summary>
    [ObservableProperty]
    public partial string RingText { get; set; }

    /// <summary>The Rings on ▾ checklist: one switch per device (PCs stay on).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RingDeviceChoiceViewModel> RingChoices { get; set; }

    /// <summary>"Delete · 3 tasks become No label".</summary>
    [ObservableProperty]
    public partial string DeleteText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConfirmingDelete))]
    public partial bool IsConfirmingDelete { get; set; }

    public bool IsNotConfirmingDelete => !IsConfirmingDelete;

    /// <summary>The label as the task list now has it (here or from another device); a name being typed stays as typed.</summary>
    public void Update(TaskLabel label, int uses, IReadOnlyList<TaskRingDevice> devices)
    {
        if (Name.Trim() == _savedName)
        {
            Name = label.Name;
        }

        _savedName = label.Name;
        OnPropertyChanged(nameof(IsNameBad));
        Color = label.Color;
        ColorBrush = TaskCategoryBrushes.Of(TaskLabelColors.ArgbHex(label.Color));
        RingOn = label.RingOn;
        _uses = uses;
        CountText = uses switch
        {
            0 => "No tasks",
            1 => "1 task",
            _ => $"{uses} tasks",
        };
        DeleteText = uses switch
        {
            0 => "Delete",
            1 => "Delete · 1 task becomes No label",
            _ => $"Delete · {uses} tasks become No label",
        };
        RingText = TaskRingText.For(label.RingOn, devices);
        bool sameDevices = devices.Select(device => device.Id).SequenceEqual(_devices.Select(device => device.Id)) && RingChoices.Count == devices.Count;
        _devices = devices;
        if (sameDevices)
        {
            foreach (RingDeviceChoiceViewModel choice in RingChoices)
            {
                choice.Show(TaskRingDevices.Rings(label.RingOn, choice.Device.Id));
            }
        }
        else
        {
            RingChoices = devices.Select(device => new RingDeviceChoiceViewModel(device, TaskRingDevices.Rings(label.RingOn, device.Id), ToggleRing)).ToList();
        }
    }

    /// <summary>Enter or clicking away: a good new name is kept; a blank or taken one is put back, with a toast saying why.</summary>
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        string trimmed = Name.Trim();
        if (trimmed == _savedName)
        {
            return;
        }

        string old = _savedName;
        TaskLabelNameCheck check = _labels.CheckName(Id, trimmed);
        if (check != TaskLabelNameCheck.Fine)
        {
            Name = old;
            _say(check == TaskLabelNameCheck.Blank ? "A label needs a name" : $"There’s already a label called “{trimmed}”");
            return;
        }

        // Saved once: Enter, then the box losing focus, doesn't save it again.
        _savedName = trimmed;
        Name = trimmed;
        await _labels.RenameAsync(Id, trimmed);
        _say($"Renamed “{old}” to “{trimmed}”");
    }

    /// <summary>Esc in the name box: the name as it was.</summary>
    [RelayCommand]
    private void CancelName()
    {
        Name = _savedName;
    }

    [RelayCommand]
    private void AskDelete()
    {
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void KeepIt()
    {
        IsConfirmingDelete = false;
    }

    /// <summary>The label goes on every device; its tasks are left with No label.</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsConfirmingDelete = false;
        string name = _savedName;
        int uses = _uses;
        await _labels.DeleteAsync(Id);
        _say(uses == 0 ? $"Deleted label “{name}”" : $"Deleted label “{name}” · {uses} now have No label");
    }

    private void ToggleRing(TaskRingDevice device, bool on)
    {
        if (device.IsPc)
        {
            if (!on)
            {
                _say(device.IsThisPc ? "This PC always rings for its own reminders" : "A PC always rings for its own reminders");
            }

            return;
        }

        // Phones only: a PC always rings, so it never holds a list back from "every device".
        IReadOnlyList<Guid> phones = _devices.Where(shown => !shown.IsPc).Select(shown => shown.Id).Where(id => id != Guid.Empty).ToList();
        IReadOnlyList<Guid>? ringOn = TaskRingDevices.Toggled(RingOn, phones, device.Id, on);
        // Kept here at once, so a second switch in the same open checklist starts from this one.
        RingOn = ringOn;
        RingText = TaskRingText.For(ringOn, _devices);
        _ = _labels.SetRingOnAsync(Id, ringOn);
    }
}
