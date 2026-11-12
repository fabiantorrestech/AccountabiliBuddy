using Avalonia.Controls.Primitives;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Keeps a group of pop-ups to one open at a time (round 2, brief §4.2: the title bar's volume popover, session menu, sync
/// popover and theme menu). Opening one closes whichever other is open, however it was opened (click, hover, hold or keyboard).
/// </summary>
public sealed class OnePopupAtATime
{
    private readonly List<FlyoutBase> _popups = [];

    /// <summary>Adds a pop-up to the group; null (a part that isn't there) is ignored.</summary>
    public void Add(FlyoutBase? popup)
    {
        if (popup is null || _popups.Contains(popup))
        {
            return;
        }

        _popups.Add(popup);
        popup.Opened += (_, _) => CloseAllBut(popup);
    }

    private void CloseAllBut(FlyoutBase opened)
    {
        foreach (FlyoutBase popup in _popups)
        {
            if (!ReferenceEquals(popup, opened) && popup.IsOpen)
            {
                popup.Hide();
            }
        }
    }
}
