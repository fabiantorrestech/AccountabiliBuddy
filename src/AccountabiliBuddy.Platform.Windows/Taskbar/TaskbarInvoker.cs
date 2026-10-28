using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// Clicks one of Windows' own taskbar buttons through UI Automation (the tray's "show hidden icons", the clock), for the overlay that sits over
/// them. It finds the button by automation id or window class and uses its Invoke pattern; nothing is written to the taskbar.
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

        var automation = (IUIAutomation)new CUIAutomation();
        IUIAutomationElementArray items = automation.ElementFromHandle(taskbar).FindAll(TreeScope.TreeScope_Descendants, automation.CreateTrueCondition());
        for (int index = 0; index < items.Length; index++)
        {
            IUIAutomationElement element = items.GetElement(index);
            string id = element.CurrentAutomationId.ToString() ?? string.Empty;
            string className = element.CurrentClassName.ToString() ?? string.Empty;
            string name = element.CurrentName.ToString() ?? string.Empty;
            bool match = automationIds.Contains(id, StringComparer.Ordinal) || classNames.Contains(className, StringComparer.Ordinal)
                         || nameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
            if (!match)
            {
                continue;
            }

            try
            {
                var pattern = (IUIAutomationInvokePattern)element.GetCurrentPattern(UIA_PATTERN_ID.UIA_InvokePatternId);
                pattern.Invoke();
                return true;
            }
            catch (Exception)
            {
                // This element has no Invoke; the next match may.
            }
        }

        return false;
    }
}
