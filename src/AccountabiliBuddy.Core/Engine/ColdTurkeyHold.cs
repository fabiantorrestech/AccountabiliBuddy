using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Decides the Cold Turkey commands for one check (ADR 37, 39), from what is going on now, what the user assigned,
/// each block's state and what the app remembers. Holding: a block wanted with a timed lock is (re)locked whenever
/// its lock ends within <see cref="RenewWithin"/>, for <see cref="LockMinutes"/>; a block wanted with the password is
/// password-locked (after any running timed lock lapses) and, when its window ends, stopped with the password and
/// started again unlocked so it stays on. A password-held block found off in its window means the user's spare key
/// was used. Without a confirmed spare key the password is never used: those blocks get the timed lock. Blocks with
/// a lock the user set in Cold Turkey, and blocks released for today, are left alone. Re-arm (ADR 37): the day's
/// "on" events start blocks that are off, Auto lift windows stop unlocked ones; a held block ignores both.
/// </summary>
public static class ColdTurkeyHold
{
    public const int LockMinutes = 10;
    public static readonly TimeSpan RenewWithin = TimeSpan.FromMinutes(5);

    /// <summary>The lock each block should have now: <see cref="HeldBlock.Timed"/>, <see cref="HeldBlock.Password"/> or null.</summary>
    public static IReadOnlyDictionary<string, string> Wanted(IReadOnlyList<HeldBlock> held, HoldWindows windows, bool passwordReady)
    {
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (HeldBlock block in held)
        {
            string? kind = null;
            if (windows.FocusSession && (block.InFocusSessions || windows.SessionBlocks.Contains(block.Name)))
            {
                kind = block.FocusLock;
            }
            else if ((windows.PreWork && block.InPreWork) || (windows.WorkHours && block.InWorkHours)
                     || block.Categories.Any(windows.Categories.Contains))
            {
                kind = block.OtherLock;
            }

            if (kind is not null)
            {
                wanted[block.Name] = kind == HeldBlock.Password && !passwordReady ? HeldBlock.Timed : kind;
            }
        }

        // A session's profile's expected blocks are held in that session even when not assigned.
        if (windows.FocusSession)
        {
            foreach (string name in windows.SessionBlocks.Where(name => !wanted.ContainsKey(name)))
            {
                wanted[name] = passwordReady ? HeldBlock.Password : HeldBlock.Timed;
            }
        }

        return wanted;
    }

    public static IReadOnlyList<ColdTurkeyAction> Plan(
        DateTime now,
        IReadOnlyList<HeldBlock> held,
        HoldWindows windows,
        ColdTurkeyBlocks blocks,
        HoldMemory memory,
        bool passwordReady,
        IReadOnlyList<BlockRearm.Event> rearm)
    {
        IReadOnlyDictionary<string, string> wanted = Wanted(held, windows, passwordReady);
        var actions = new List<ColdTurkeyAction>();
        foreach (ColdTurkeyBlockState block in blocks.Blocks)
        {
            string name = block.Name;
            bool ours = memory.PasswordHeld.Contains(name);
            if (memory.Released.Contains(name))
            {
                if (ours && block.PasswordLocked)
                {
                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.StopPassword, name, "released for today"));
                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.Start, name, "released for today"));
                }

                continue;
            }

            wanted.TryGetValue(name, out string? want);
            if (ours && !block.IsSwitchedOn)
            {
                if (want == HeldBlock.Password)
                {
                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.SpareKeyUsed, name, "turned off during its window"));
                }

                continue;
            }

            // A lock the user set in Cold Turkey (or a password that isn't ours) is theirs.
            if (block.OtherLock is not null || (block.PasswordLocked && !ours))
            {
                continue;
            }

            switch (want)
            {
                case HeldBlock.Password when !block.PasswordLocked:
                    if (block.LockedUntil is DateTime until && until > now)
                    {
                        break; // A running timed lock can't be changed; it's password-locked once it lapses.
                    }

                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.LockPassword, name, "window started"));
                    break;
                case HeldBlock.Timed:
                    if (ours && block.PasswordLocked)
                    {
                        actions.Add(new ColdTurkeyAction(ColdTurkeyAction.StopPassword, name, "now a timed hold"));
                        actions.Add(new ColdTurkeyAction(ColdTurkeyAction.LockTimed, name, "now a timed hold"));
                    }
                    else if (block.LockedUntil is not DateTime lockedUntil || lockedUntil - now < RenewWithin || !block.IsSwitchedOn)
                    {
                        actions.Add(new ColdTurkeyAction(ColdTurkeyAction.LockTimed, name, "held"));
                    }

                    break;
                case null when ours && block.PasswordLocked:
                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.StopPassword, name, "window ended"));
                    actions.Add(new ColdTurkeyAction(ColdTurkeyAction.Start, name, "window ended"));
                    break;
            }
        }

        // Re-arm (ADR 37): the last event per block wins; held, released and locked blocks are left out.
        var last = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (BlockRearm.Event e in rearm.OrderBy(e => e.At))
        {
            IEnumerable<string> names = e.Blocks?.Where(b => b.Kind == BlockRef.ColdTurkeyBlock).Select(b => b.Id) ?? memory.Known;
            foreach (string name in names)
            {
                last[name] = e.TurnOn;
            }
        }

        foreach ((string name, bool turnOn) in last)
        {
            ColdTurkeyBlockState? block = blocks.Blocks.FirstOrDefault(b => b.Name == name);
            if (block is null || wanted.ContainsKey(name) || memory.Released.Contains(name) || block.IsLockedAt(now)
                || actions.Any(a => a.Block == name))
            {
                continue;
            }

            if (turnOn && !block.IsSwitchedOn)
            {
                actions.Add(new ColdTurkeyAction(ColdTurkeyAction.Start, name, "the day's re-enable time"));
            }
            else if (!turnOn && block.IsSwitchedOn)
            {
                actions.Add(new ColdTurkeyAction(ColdTurkeyAction.Stop, name, "a lift window started"));
            }
        }

        return actions;
    }
}
