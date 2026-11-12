using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.Services.Reminders;

/// <summary>
/// What a reminder card shows for a reminder of an <see cref="IReminderSource"/> (a goal reminder: the goal's icon and name, the
/// reminder's reason, a progress line, Log or Tick, Open goal). Holds the user's private text (names, reasons): shown on screen only,
/// never logged.
/// </summary>
/// <param name="Head">"Goal reminder · 8:00 PM" (the ringer adds "· 2 more").</param>
/// <param name="Title">The item's name.</param>
/// <param name="Reason">The reminder's reason ("if not done yet"); empty for none.</param>
/// <param name="Meta">The progress line: "8 / 15 min today · On track".</param>
/// <param name="Icon">The item's icon.</param>
/// <param name="IconBrush">The item's colour, for the icon.</param>
/// <param name="PrimaryText">"Tick today", "Log a value…", "Add time…" or "Open goal".</param>
/// <param name="PrimaryIcon">The accent button's icon.</param>
/// <param name="OpenText">The second action: "Open goal".</param>
public sealed record ReminderSourceCard(
    string Head,
    string Title,
    string Reason,
    string Meta,
    MaterialIconKind Icon,
    IBrush IconBrush,
    string PrimaryText,
    MaterialIconKind PrimaryIcon,
    string OpenText);
