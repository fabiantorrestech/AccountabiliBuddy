using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// One walk over the taskbar's UI Automation tree with a cache request: the properties the overlay needs come back in a single round trip
/// to Explorer instead of one call per property and element. Strings are freed as they are read and COM objects are released.
/// </summary>
internal static class UiaTaskbarReader
{
    /// <summary>What one taskbar element looked like; <see cref="Element"/> is kept only when asked for (to invoke it), and then the caller releases it.</summary>
    public sealed record Item(RECT Rect, string ClassName, string AutomationId, string Name, UIA_CONTROLTYPE_ID ControlType, IUIAutomationElement? Element);

    public static List<Item> Read(HWND taskbar, bool keepElements = false)
    {
        var automation = (IUIAutomation)new CUIAutomation();
        IUIAutomationCacheRequest cache = automation.CreateCacheRequest();
        cache.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
        cache.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
        cache.AddProperty(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId);
        cache.AddProperty(UIA_PROPERTY_ID.UIA_NamePropertyId);
        cache.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
        IUIAutomationElement root = automation.ElementFromHandle(taskbar);
        IUIAutomationElementArray items = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, automation.CreateTrueCondition(), cache);
        var found = new List<Item>();
        int count = items.Length;
        for (int index = 0; index < count; index++)
        {
            IUIAutomationElement element = items.GetElement(index);
            found.Add(new Item(
                element.CachedBoundingRectangle,
                UiaText.Take(element.CachedClassName),
                UiaText.Take(element.CachedAutomationId),
                UiaText.Take(element.CachedName),
                element.CachedControlType,
                keepElements ? element : null));
            if (!keepElements)
            {
                Marshal.ReleaseComObject(element);
            }
        }

        Marshal.ReleaseComObject(items);
        Marshal.ReleaseComObject(root);
        Marshal.ReleaseComObject(cache);
        Marshal.ReleaseComObject(automation);
        return found;
    }
}
