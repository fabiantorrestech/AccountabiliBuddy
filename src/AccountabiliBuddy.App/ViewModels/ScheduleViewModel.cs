using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Schedule tab: the week grid plus an editor for the selected block.
/// Changes save immediately. Locked during a focus phase.
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
            document with { Schedule = new WeeklySchedule(change(document.Schedule.Blocks).ToList()) });
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
