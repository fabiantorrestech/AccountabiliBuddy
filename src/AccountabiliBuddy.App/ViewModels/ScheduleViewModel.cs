using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Schedule tab: the week grid plus an editor for the selected block, and planned off days.
/// Changes save immediately; looser ones (deleting or shortening a block, adding an off day) count down first and
/// the grid shows them as saved (see <see cref="PendingChangesViewModel"/>). The grid is locked during a focus phase;
/// off days can still be added (they wait for the countdown and the end of the session) and removed (that only tightens).
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public ScheduleViewModel(ConfigurationService configuration, SessionController sessions, PendingChangesViewModel pendingChanges)
    {
        _configuration = configuration;
        _sessions = sessions;
        PendingChanges = pendingChanges;

        Items = Array.Empty<ScheduleBlockItem>();
        NewOffDay = FirstOffDay;

        _configuration.Changed += (_, _) => RefreshFromConfiguration(_configuration.Editing);
        _configuration.PendingChanged += (_, _) => RefreshFromConfiguration(_configuration.Editing);
        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;

        RefreshFromConfiguration(_configuration.Editing);
    }

    public PendingChangesViewModel PendingChanges { get; }

    /// <summary>Monday-first, matching the grid.</summary>
    public static IReadOnlyList<DayOfWeek> Days { get; } = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public ObservableCollection<ProfileOption> Profiles { get; } = new();

    /// <summary>Replaced (not mutated) on every change so the grid redraws.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ScheduleBlockItem> Items { get; set; }

    /// <summary>Profile given to blocks drawn on the grid.</summary>
    [ObservableProperty]
    public partial ProfileOption? PaintProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial Guid? SelectedBlockId { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial DayOfWeek EditDay { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EditStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EditEnd { get; set; }

    [ObservableProperty]
    public partial ProfileOption? EditProfile { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ObservableCollection<OffDayRowViewModel> OffDays { get; } = new();

    /// <summary>The date picked for a new off day; today at the earliest.</summary>
    [ObservableProperty]
    public partial DateTime? NewOffDay { get; set; }

    [ObservableProperty]
    public partial string? OffDayMessage { get; set; }

    public DateTime FirstOffDay => Today;

    private DateTime Today => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).Date;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateBlockCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsLocked { get; set; }

    private bool CanEdit => !IsLocked;

    private bool CanEditSelection => !IsLocked && SelectedBlockId is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CreateBlockAsync(ScheduleSlot slot)
    {
        if (PaintProfile is null)
        {
            StatusMessage = "Create a profile in the Profiles tab first.";
            return;
        }

        var block = new ScheduleBlock(Guid.NewGuid(), slot.Day, slot.Start, slot.End, PaintProfile.Id);
        PendingChanges? waiting = await ReplaceBlocksAsync(blocks => blocks.Append(block));
        SelectedBlockId = block.Id;
        StatusMessage = Describe("Block added.", waiting);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task ApplyEditAsync()
    {
        if (EditStart is not TimeSpan start || EditEnd is not TimeSpan end || EditProfile is null)
        {
            StatusMessage = "Choose a start time, end time and profile.";
            return;
        }

        if (start == end)
        {
            StatusMessage = "Start and end must differ. For a block past midnight, set the end earlier than the start.";
            return;
        }

        Guid id = SelectedBlockId!.Value;
        var updated = new ScheduleBlock(id, EditDay, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end), EditProfile.Id);
        PendingChanges? waiting = await ReplaceBlocksAsync(blocks => blocks.Select(block => block.Id == id ? updated : block));
        StatusMessage = Describe("Block updated.", waiting);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task DeleteSelectedAsync()
    {
        Guid id = SelectedBlockId!.Value;
        SelectedBlockId = null;
        PendingChanges? waiting = await ReplaceBlocksAsync(blocks => blocks.Where(block => block.Id != id));
        StatusMessage = Describe("Block deleted.", waiting);
    }

    [RelayCommand]
    private async Task AddOffDayAsync()
    {
        if (NewOffDay is not DateTime picked)
        {
            OffDayMessage = "Pick a date first.";
            return;
        }

        if (picked.Date < Today)
        {
            OffDayMessage = "That day is already over.";
            return;
        }

        DateOnly date = DateOnly.FromDateTime(picked);
        if (_configuration.Editing.Schedule.OffDays.Contains(date))
        {
            OffDayMessage = "That day is already an off day.";
            return;
        }

        PendingChanges? waiting = await ReplaceOffDaysAsync(days => days.Append(date));
        OffDayMessage = Describe($"No scheduled blocks on {date:dddd d MMMM}.", waiting);
    }

    [RelayCommand]
    private async Task RemoveOffDayAsync(OffDayRowViewModel row)
    {
        PendingChanges? waiting = await ReplaceOffDaysAsync(days => days.Where(day => day != row.Date));
        OffDayMessage = Describe($"{row.Date:dddd d MMMM} is a normal day again.", waiting);
    }

    partial void OnSelectedBlockIdChanged(Guid? value)
    {
        ScheduleBlock? block = _configuration.Editing.Schedule.Blocks.FirstOrDefault(candidate => candidate.Id == value);
        HasSelection = block is not null;
        if (block is null)
        {
            return;
        }

        EditDay = block.Day;
        EditStart = block.Start.ToTimeSpan();
        EditEnd = block.End.ToTimeSpan();
        EditProfile = Profiles.FirstOrDefault(option => option.Id == block.ProfileId);
    }

    /// <summary>What a save did, mentioning the countdown when something is still waiting.</summary>
    private static string Describe(string done, PendingChanges? waiting)
    {
        return waiting is null ? done : $"{done} Looser changes wait for the countdown above.";
    }

    private Task<PendingChanges?> ReplaceBlocksAsync(Func<IEnumerable<ScheduleBlock>, IEnumerable<ScheduleBlock>> change)
    {
        return _configuration.UpdateAsync(document =>
            document with { Schedule = document.Schedule with { Blocks = change(document.Schedule.Blocks).ToList() } });
    }

    private Task<PendingChanges?> ReplaceOffDaysAsync(Func<IEnumerable<DateOnly>, IEnumerable<DateOnly>> change)
    {
        // Past off days are dropped whenever the list changes.
        DateOnly today = DateOnly.FromDateTime(Today);
        return _configuration.UpdateAsync(document => document with
        {
            Schedule = document.Schedule with
            {
                OffDays = change(document.Schedule.OffDays).Where(day => day >= today).Distinct().Order().ToList(),
            },
        });
    }

    private void RefreshFromConfiguration(ProfileDocument document)
    {
        PaintProfile = ProfileOptions.Refresh(Profiles, document.Profiles, PaintProfile?.Id);

        Items = document.Schedule.Blocks
            .Select(block => new ScheduleBlockItem(
                block,
                ProfileOptions.NameOf(document.Profiles, block.ProfileId) ?? "(deleted profile)",
                ProfileColors.For(block.ProfileId)))
            .ToList();

        DateOnly today = DateOnly.FromDateTime(Today);
        OffDays.Clear();
        foreach (DateOnly day in document.Schedule.OffDays.Where(day => day >= today).Order())
        {
            OffDays.Add(new OffDayRowViewModel(day));
        }

        if (SelectedBlockId is Guid id && document.Schedule.Blocks.All(block => block.Id != id))
        {
            SelectedBlockId = null;
        }
        else
        {
            // Re-read the selected block so the editor shows saved values and fresh profile options.
            OnSelectedBlockIdChanged(SelectedBlockId);
        }
    }
}
