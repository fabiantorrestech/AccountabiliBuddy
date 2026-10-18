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
/// Changes save immediately. The grid is locked during a focus phase; off days can always be added for tomorrow
/// or later (planning ahead doesn't loosen today) and removed (that only tightens). For today, use Unlock.
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public ScheduleViewModel(ConfigurationService configuration, SessionController sessions)
    {
        _configuration = configuration;
        _sessions = sessions;

        Items = Array.Empty<ScheduleBlockItem>();
        NewOffDay = Tomorrow;

        _configuration.Changed += (_, document) => RefreshFromConfiguration(document);
        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;

        RefreshFromConfiguration(_configuration.Current);
    }

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

    /// <summary>The date picked for a new off day; tomorrow at the earliest.</summary>
    [ObservableProperty]
    public partial DateTime? NewOffDay { get; set; }

    [ObservableProperty]
    public partial string? OffDayMessage { get; set; }

    public DateTime Tomorrow => Today.AddDays(1);

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
        await ReplaceBlocksAsync(blocks => blocks.Append(block));
        SelectedBlockId = block.Id;
        StatusMessage = "Block added.";
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
        await ReplaceBlocksAsync(blocks => blocks.Select(block => block.Id == id ? updated : block));
        StatusMessage = "Block updated.";
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task DeleteSelectedAsync()
    {
        Guid id = SelectedBlockId!.Value;
        SelectedBlockId = null;
        await ReplaceBlocksAsync(blocks => blocks.Where(block => block.Id != id));
        StatusMessage = "Block deleted.";
    }

    [RelayCommand]
    private async Task AddOffDayAsync()
    {
        if (NewOffDay is not DateTime picked)
        {
            OffDayMessage = "Pick a date first.";
            return;
        }

        if (picked.Date < Tomorrow)
        {
            OffDayMessage = "Off days start tomorrow at the earliest. For today, use Unlock on the Session tab.";
            return;
        }

        DateOnly date = DateOnly.FromDateTime(picked);
        if (_configuration.Current.Schedule.OffDays.Contains(date))
        {
            OffDayMessage = "That day is already an off day.";
            return;
        }

        await ReplaceOffDaysAsync(days => days.Append(date));
        OffDayMessage = $"No scheduled blocks on {date:dddd d MMMM}.";
    }

    [RelayCommand]
    private async Task RemoveOffDayAsync(OffDayRowViewModel row)
    {
        await ReplaceOffDaysAsync(days => days.Where(day => day != row.Date));
        OffDayMessage = $"{row.Date:dddd d MMMM} is a normal day again.";
    }

    partial void OnSelectedBlockIdChanged(Guid? value)
    {
        ScheduleBlock? block = _configuration.Current.Schedule.Blocks.FirstOrDefault(candidate => candidate.Id == value);
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

    private Task ReplaceBlocksAsync(Func<IEnumerable<ScheduleBlock>, IEnumerable<ScheduleBlock>> change)
    {
        return _configuration.UpdateAsync(document =>
            document with { Schedule = document.Schedule with { Blocks = change(document.Schedule.Blocks).ToList() } });
    }

    private Task ReplaceOffDaysAsync(Func<IEnumerable<DateOnly>, IEnumerable<DateOnly>> change)
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
