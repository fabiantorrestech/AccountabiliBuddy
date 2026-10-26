using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One Cold Turkey block in the setup assistant's "Let AB hold the keys" step (ADR 39): whether its Cold Turkey
/// settings let the app hold it, and where to hold it.
/// </summary>
public sealed partial class ColdTurkeySetupRow : ObservableObject
{
    public ColdTurkeySetupRow(ColdTurkeyBlockState block, HeldBlock? held, DateTime now)
    {
        Name = block.Name;
        InFocusSessions = held?.InFocusSessions ?? false;
        InPreWork = held?.InPreWork ?? false;
        InWorkHours = held?.InWorkHours ?? false;
        Categories = held?.Categories ?? Array.Empty<Guid>();
        HardInFocus = (held?.FocusLock ?? HeldBlock.Password) == HeldBlock.Password;
        HardElsewhere = held?.OtherLock == HeldBlock.Password;
        Update(block, now);
    }

    public string Name { get; }

    /// <summary>Day plan categories it's held in (kept as they were; set from the Day plan later).</summary>
    public IReadOnlyList<Guid> Categories { get; }

    [ObservableProperty]
    public partial bool InFocusSessions { get; set; }

    [ObservableProperty]
    public partial bool InPreWork { get; set; }

    [ObservableProperty]
    public partial bool InWorkHours { get; set; }

    /// <summary>The password lock in focus sessions (else timed).</summary>
    [ObservableProperty]
    public partial bool HardInFocus { get; set; }

    /// <summary>The password lock in Pre-Work and work hours (else timed, the default).</summary>
    [ObservableProperty]
    public partial bool HardElsewhere { get; set; }

    /// <summary>What to change in Cold Turkey for this block, or "Ready".</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    public bool IsUsed => InFocusSessions || InPreWork || InWorkHours || Categories.Count > 0;

    /// <summary>Re-reads the block's Cold Turkey settings into the checklist.</summary>
    public void Update(ColdTurkeyBlockState block, DateTime now)
    {
        var todo = new List<string>();
        if (block.OtherLock is not null || (block.PasswordLocked && !block.IsSwitchedOn))
        {
            todo.Add("set its lock to None in Cold Turkey (AccountabiliBuddy does the locking)");
        }

        if (block.UnblocksWhenLockEnds)
        {
            todo.Add("turn off \"unblock when the lock ends\" so it stays on after a timed lock");
        }

        IsReady = todo.Count == 0;
        Status = IsReady ? "✓ Ready" : "To do: " + string.Join("; ", todo);
    }

    public HeldBlock ToHeld() => new(
        Name, InFocusSessions, InPreWork, InWorkHours, Categories,
        HardInFocus ? HeldBlock.Password : HeldBlock.Timed,
        HardElsewhere ? HeldBlock.Password : HeldBlock.Timed);
}
