using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// Clicks one of Windows' own taskbar buttons through UI Automation (the tray's "show hidden icons", the clock), for the overlay that sits over
/// them. It finds the button by automation id, class or name and uses its Invoke pattern; nothing is written to the taskbar.
/// </summary>
internal static class TaskbarInvoker
{
    public static bool Invoke(string[] automationIds, string[] classNames, string[] nameParts)
    {
        HWND taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (taskbar.IsNull)
        {
            return false;
        }

        List<UiaTaskbarReader.Item> items = UiaTaskbarReader.Read(taskbar, keepElements: true);
        bool invoked = false;
        try
        {
            foreach (UiaTaskbarReader.Item item in items)
            {
                bool match = automationIds.Contains(item.AutomationId, StringComparer.Ordinal) || classNames.Contains(item.ClassName, StringComparer.Ordinal)
                             || nameParts.Any(part => item.Name.Contains(part, StringComparison.OrdinalIgnoreCase));
                if (!match || invoked)
                {
                    continue;
                }

                try
                {
                    var pattern = (IUIAutomationInvokePattern)item.Element!.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId);
                    pattern.Invoke();
                    invoked = true;
                }
                catch (Exception)
                {
                    // This element has no Invoke; the next match may.
                }
            }
        }
        finally
        {
            foreach (UiaTaskbarReader.Item item in items)
            {
                if (item.Element is { } element)
                {
                    Marshal.ReleaseComObject(element);
                }
            }
        }

        return invoked;
    }
}
