using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// The computer types' starting switches (the computer-types design's table) and applying them. Applying sets only
/// these switches: never profiles, the day plan or anything else, so a type can be applied again at any time.
/// </summary>
public static class ComputerTypePreset
{
    public static PresetValues For(ComputerType type)
    {
        return type switch
        {
            ComputerType.Work => new PresetValues(
                ScreenReading: false, KeepTitlesAndAddresses: false, MaxLevel: EscalationLevel.Overlay,
                LinkOn: false, FolderOn: true, QrOn: true, FileShareOn: false,
                GoogleCalendarOn: false, CheckForUpdates: true, ColdTurkeyOn: false),
            ComputerType.LocalOnly => new PresetValues(
                ScreenReading: false, KeepTitlesAndAddresses: true, MaxLevel: EscalationLevel.Enforce,
                LinkOn: false, FolderOn: false, QrOn: false, FileShareOn: false,
                GoogleCalendarOn: false, CheckForUpdates: false, ColdTurkeyOn: true),
            _ => new PresetValues(
                ScreenReading: false, KeepTitlesAndAddresses: true, MaxLevel: EscalationLevel.Enforce,
                LinkOn: true, FolderOn: false, QrOn: false, FileShareOn: false,
                GoogleCalendarOn: true, CheckForUpdates: true, ColdTurkeyOn: true),
        };
    }

    /// <summary>
    /// Sets the switches the type names. The sync folder goes on only once a folder is chosen (the assistant asks);
    /// screen reading is only ever switched off here (switching it on asks first, in Setup).
    /// </summary>
    public static async Task ApplyAsync(
        PresetValues values,
        PrivacySettingsService privacy,
        EnforcementLimitService limit,
        SyncSettingsService sync,
        UpdateService? updates)
    {
        if (!values.ScreenReading && privacy.Current.ScreenReadingEnabled)
        {
            await privacy.SetScreenReadingAsync(false);
        }

        await privacy.SetKeepTitlesAndAddressesAsync(values.KeepTitlesAndAddresses);
        await limit.SaveAsync(values.MaxLevel);
        SyncSettings current = sync.Current;
        await sync.SaveAsync(current with
        {
            LinkOn = values.LinkOn,
            FolderOn = values.FolderOn && current.FolderPath is not null,
            QrOn = values.QrOn,
            FileShareOn = values.FileShareOn,
        });
        if (updates is not null)
        {
            await updates.SetCheckForUpdatesAsync(values.CheckForUpdates);
        }
    }
}
