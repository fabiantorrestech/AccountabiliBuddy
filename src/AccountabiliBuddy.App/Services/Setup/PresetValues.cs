using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>A computer type's starting switches (the computer-types design's table).</summary>
/// <param name="ScreenReading">Screen reading (OCR); off on every type (Personal asks later).</param>
/// <param name="KeepTitlesAndAddresses">History keeps window titles, full addresses and screen text (encrypted).</param>
/// <param name="MaxLevel">How far escalation goes: Overlay is "notice and overlay, never minimize".</param>
/// <param name="LinkOn">Sync files over the network link to paired devices.</param>
/// <param name="FolderOn">The sync folder; it still needs a folder chosen before it's switched on.</param>
/// <param name="QrOn">Today's QR codes for the phone, with the end-of-day reminder.</param>
/// <param name="FileShareOn">Sync files saved and opened by hand.</param>
/// <param name="GoogleCalendarOn">Google Calendar sign-in offered (iCal links stay available either way).</param>
/// <param name="CheckForUpdates">The app checks the releases repo on its own.</param>
/// <param name="ColdTurkeyOn">Cold Turkey integration offered.</param>
public sealed record PresetValues(
    bool ScreenReading,
    bool KeepTitlesAndAddresses,
    EscalationLevel MaxLevel,
    bool LinkOn,
    bool FolderOn,
    bool QrOn,
    bool FileShareOn,
    bool GoogleCalendarOn,
    bool CheckForUpdates,
    bool ColdTurkeyOn);
