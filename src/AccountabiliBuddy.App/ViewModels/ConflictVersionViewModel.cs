using System.Windows.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One version in "Changed on both devices" (spec 05 §11): the device that made it (icon and name), its text, what changed ("title"
/// or "note") and when, in large 12-hour type. The newer one is listed first and highlighted with the "Newer" tag. Holds the user's
/// text: shown only.
/// </summary>
/// <param name="Text">The title or note of this version ("(no note)" for a removed note).</param>
/// <param name="Sub">"Work PC (this PC) · title".</param>
/// <param name="TimeText">"10:44 AM", or "Tue 11/10 10:44 AM" on another day.</param>
/// <param name="Keep">Keeps this version and closes the dialog.</param>
public sealed record ConflictVersionViewModel(string Text, string Sub, string TimeText, MaterialIconKind DeviceIcon, bool IsNewer, bool IsEmpty, ICommand Keep)
{
    public string KeepName => "Keep this version: " + Sub;
}
