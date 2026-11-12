namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Whether the Tasks page shows the Shelf's columns (round 2 R3, change note §2 "Tasks during a session"). Planning: always. In the
/// session layout with Settings › Tasks › During a session › Hide the Shelf on: hidden behind "Show the Shelf" (or Ctrl+\) until the
/// user shows it; shown, it stays at full strength until they hide it again, leave Tasks, hold Reset, or the layout ends. Only what the
/// page draws: nothing here changes a session.
/// </summary>
public sealed class TaskShelfVisibility
{
    /// <summary>The session layout shows (a session runs, or one ended and Tasks hasn't switched back yet).</summary>
    public bool InSessionLayout { get; private set; }

    /// <summary>The layout started with Hide the Shelf on (kept for the whole layout, like the layout's other settings).</summary>
    public bool HidesInSession { get; private set; }

    /// <summary>The user showed the Shelf during this layout (and hasn't hidden it or left Tasks since).</summary>
    public bool ShownByUser { get; private set; }

    /// <summary>The Shelf's columns show.</summary>
    public bool IsShown => !CanToggle || ShownByUser;

    /// <summary>Show the Shelf / Ctrl+\ do something: only in the session layout with Hide the Shelf on.</summary>
    public bool CanToggle => InSessionLayout && HidesInSession;

    /// <summary>A session layout starts: the Shelf hides when <paramref name="hideShelf"/> (the setting as it is now).</summary>
    public void StartSessionLayout(bool hideShelf)
    {
        InSessionLayout = true;
        HidesInSession = hideShelf;
        ShownByUser = false;
    }

    /// <summary>Back to planning: the Shelf shows.</summary>
    public void EndSessionLayout()
    {
        InSessionLayout = false;
        HidesInSession = false;
        ShownByUser = false;
    }

    /// <summary>Show the Shelf / Ctrl+\: shows or hides it again. False when it can't be hidden now (planning).</summary>
    public bool Toggle()
    {
        if (!CanToggle)
        {
            return false;
        }

        ShownByUser = !ShownByUser;
        return true;
    }

    /// <summary>Shows it (Ctrl+G found a task on it). False when it already shows.</summary>
    public bool Show()
    {
        if (IsShown)
        {
            return false;
        }

        ShownByUser = true;
        return true;
    }

    /// <summary>The user left the Tasks page, or held Reset: hidden again (when the layout hides it).</summary>
    public void HideAgain()
    {
        ShownByUser = false;
    }
}
