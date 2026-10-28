using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// Finds where a horizontal taskbar's app icons end and its tray begins, by reading the taskbar's own buttons through UI Automation
/// (ADR 51). This is the one place that guesses at Explorer's insides: a Windows update that renames its parts makes the reading
/// come back empty, and the caller then falls back to "next to the tray". Nothing is written to the taskbar. Slow (it talks to
/// Explorer), so call it off the UI thread.
/// </summary>
internal sealed class TaskbarContentProbe
{
    // Buttons the taskbar draws for apps, Start, Search, Task view and Widgets; anything else (a container, a tooltip) is skipped.
    private static readonly HashSet<int> ButtonTypes =
    [
        (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId,
        (int)UIA_CONTROLTYPE_ID.UIA_SplitButtonControlTypeId,
        (int)UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId,
        (int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId,
    ];

    // The tray's parts on Windows 11's XAML taskbar (class names start with "SystemTray", the rest are automation ids).
    private static readonly HashSet<string> TrayIds = new(StringComparer.Ordinal)
    {
        "SystemTrayIcon", "NotificationChevron", "ControlCenterButton", "ClockButton", "NotificationCenterButton",
    };

    /// <returns>Where the app icons end (x, physical pixels) and where the tray starts; each null when it could not be read.</returns>
    public (int? AppsEnd, int? TrayStart) Probe(HWND taskbar, PixelBox bounds)
    {
        var automation = (IUIAutomation)new CUIAutomation();
        IUIAutomationElement root = automation.ElementFromHandle(taskbar);
        IUIAutomationCondition all = automation.CreateTrueCondition();
        IUIAutomationElementArray items = root.FindAll(TreeScope.TreeScope_Descendants, all);
        int count = items.Length;

        var buttons = new List<(int Left, int Right)>();
        int? trayStart = null;
        for (int index = 0; index < count; index++)
        {
            IUIAutomationElement element = items.GetElement(index);
            RECT rect = element.CurrentBoundingRectangle;
            if (rect.right <= rect.left || rect.bottom <= rect.top || rect.top < bounds.Top - 2 || rect.bottom > bounds.Bottom + 2
                || rect.left < bounds.Left - 2 || rect.right > bounds.Right + 2 || rect.right - rect.left > bounds.Width / 2)
            {
                continue;
            }

            string classText = element.CurrentClassName.ToString() ?? string.Empty;
            string idText = element.CurrentAutomationId.ToString() ?? string.Empty;
            if (classText.StartsWith("SystemTray", StringComparison.Ordinal) || TrayIds.Contains(idText))
            {
                trayStart = trayStart is int known ? Math.Min(known, rect.left) : rect.left;
                continue;
            }

            if (ButtonTypes.Contains((int)element.CurrentControlType))
            {
                buttons.Add((rect.left, rect.right));
            }
        }

        // Only buttons left of the tray count as app icons (the classic taskbar's tray buttons sit inside its notification area).
        IEnumerable<(int Left, int Right)> apps = trayStart is int tray ? buttons.Where(button => button.Right <= tray + 2) : buttons;
        int? appsEnd = apps.Any() ? apps.Max(button => button.Right) : null;
        return (appsEnd, trayStart);
    }
}
