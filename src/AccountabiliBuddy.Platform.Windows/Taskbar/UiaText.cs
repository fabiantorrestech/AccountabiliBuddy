using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>Reads the BSTRs UI Automation hands back and frees them (each call allocates native memory that is ours to free).</summary>
internal static class UiaText
{
    public static unsafe string Take(BSTR text)
    {
        try
        {
            return text.ToString() ?? string.Empty;
        }
        finally
        {
            Marshal.FreeBSTR(new IntPtr(text.Value));
        }
    }
}
