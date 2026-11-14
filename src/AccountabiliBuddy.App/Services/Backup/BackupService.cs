using System.Globalization;
using System.Reflection;
using System.Text.Json;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Exports and restores settings and data as a backup file (*.abbackup, see <see cref="BackupEnvelope"/>).
/// <list type="bullet">
/// <item>With a password, secrets are included in plain text inside the encrypted file: calendar addresses, Google
/// sign-ins and client secret, and history titles, addresses, screen text and reasons. Without a password they are
/// left out (and calendar links and Google accounts with them, as they can't work without).</item>
/// <item>Never exported, as they belong to this computer: the running session, current unlock, pending changes,
/// clock anchor, calendar cache, setup-assistant state.</item>
/// <item>Restore overwrites or merges each chosen part. Before an overwrite the current state is saved to the data
/// folder's "backups" folder (for this computer only), so it can be undone. Profiles go through
/// <see cref="ConfigurationService"/>, so looser changes get the usual countdown.</item>
/// </list>
/// </summary>
public sealed class BackupService
{
    public const string CalendarSourcesKey = "calendar.sources";
    public const string CalendarEventsKey = "calendar.events";
    public const string CalendarSettingsKey = "calendar.settings";

    /// <summary>App documents that make up the "settings" part.</summary>
    public static readonly IReadOnlyList<string> SettingsKeys = new[]
    {
        "quickSession", "privacy", "integrations.browsers.watched", "integrations.coldTurkey.sightings", CalendarSettingsKey,
        // Also read by the Android companion when it imports a backup.
        "sleep", "daybar",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly SqliteActivityLog _activityLog;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly string _backupFolder;

    public BackupService(
        ConfigurationService configuration,
        SqliteJsonDocumentStore documents,
        SqliteActivityLog activityLog,
        IDataProtector protector,
        TimeProvider time,
        string dataDirectory)
    {
        _configuration = configuration;
        _documents = documents;
        _activityLog = activityLog;
        _protector = protector;
        _time = time;
        _backupFolder = Path.Combine(dataDirectory, "backups");
    }

    /// <summary>ADR 71: the synced settings besides the profiles, read and restored through their services. Set by the app.</summary>
    public SyncedSettingsAccess? Synced { get; set; }

    /// <summary>ADR 71: the task lists in their sync form. Set by the app.</summary>
    public TaskListsAccess? Tasks { get; set; }

    /// <summary>Round 2 R4: this PC's goals, carried with the task lists part (notes only with a password). Set by the app.</summary>
    public GoalsAccess? Goals { get; set; }

    /// <summary>
    /// ADR 71: device settings with a looser-change wait of their own (quit guard, PC-off), restored through their services so an
    /// old backup can't loosen them at once. Each returns a note to show, or null. Set by the app.
    /// </summary>
    public Dictionary<string, Func<JsonElement, Task<string?>>> DeviceRestorers { get; } = new(StringComparer.Ordinal);

    /// <summary>ADR 71: this PC shares its task lists, so synced settings backups carry them. Set by the app.</summary>
    public Func<bool> ShareTaskLists { get; set; } = () => true;

    /// <summary>
    /// ADR 71: exports one kind of backup. With a password the file is encrypted and holds secrets (calendar links, Google sign-ins);
    /// without one it holds none.
    /// </summary>
    public async Task ExportKindAsync(BackupKind kind, string path, string? password)
    {
        string json = await CreateKindJsonAsync(kind, includeSecrets: password is not null);
        string file = BackupEnvelope.Seal(json, password is not null ? BackupProtection.Password : BackupProtection.None, password);
        await File.WriteAllTextAsync(path, file);
    }

    /// <summary>ADR 71: one kind's content as JSON (what an export or an auto-backup writes).</summary>
    public async Task<string> CreateKindJsonAsync(BackupKind kind, bool includeSecrets = false)
    {
        BackupContent content = await CreateContentAsync(BackupKinds.Parts(kind, ShareTaskLists()), includeSecrets);
        content = content with { Kind = BackupKinds.Name(kind), Platform = BackupKinds.PlatformPc };
        return JsonSerializer.Serialize(content, SerializerOptions);
    }

    /// <summary>Stored documents that belong to no list of <see cref="DeviceSettingsRegistry"/> (a dev-build check).</summary>
    public async Task<IReadOnlyList<string>> UnclassifiedKeysAsync() => DeviceSettingsRegistry.Unclassified(await _documents.ListKeysAsync());

    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    /// <param name="password">Null for a backup without secrets (not encrypted); otherwise secrets are included and the file is encrypted with it.</param>
    public async Task ExportAsync(string path, BackupParts parts, string? password)
    {
        bool includeSecrets = password is not null;
        BackupContent content = await CreateContentAsync(parts, includeSecrets);
        string file = BackupEnvelope.Seal(
            JsonSerializer.Serialize(content, SerializerOptions),
            includeSecrets ? BackupProtection.Password : BackupProtection.None,
            password);
        await File.WriteAllTextAsync(path, file);
    }

    /// <exception cref="FormatException">Not a backup file, damaged, or from a newer version.</exception>
    public static BackupProtection Inspect(string fileText)
    {
        return BackupEnvelope.Inspect(fileText);
    }

    /// <exception cref="FormatException">Not a backup file, damaged, or from a newer version.</exception>
    /// <exception cref="BackupPasswordException">A password is needed, or it's wrong.</exception>
    public BackupContent Read(string fileText, string? password)
    {
        string json = BackupEnvelope.Open(fileText, password, _protector);
        try
        {
            return JsonSerializer.Deserialize<BackupContent>(json, SerializerOptions)
                   ?? throw new FormatException("The backup is empty.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("The backup's content is damaged.", ex);
        }
    }

    /// <summary>ADR 71: a backup's content JSON as it comes out of a sealed Drive copy.</summary>
    /// <exception cref="FormatException">Not a backup's content.</exception>
    public static BackupContent ReadContent(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupContent>(json, SerializerOptions) ?? throw new FormatException("The backup is empty.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("The backup's content is damaged.", ex);
        }
    }

    /// <summary>Whether this app already has something in the given parts, so the user is asked to overwrite or merge.</summary>
    public async Task<bool> HasExistingDataAsync(BackupParts parts)
    {
        if (parts.HasFlag(BackupParts.Profiles) && _configuration.Current.Profiles.Count > 0)
        {
            return true;
        }

        if (parts.HasFlag(BackupParts.Settings))
        {
            foreach (string key in SettingsKeys)
            {
                if (await _documents.LoadAsync<JsonElement?>(key) is not null)
                {
                    return true;
                }
            }
        }

        if (parts.HasFlag(BackupParts.Calendars)
            && ((await _documents.LoadAsync<List<CalendarSource>>(CalendarSourcesKey))?.Any(source => source.Kind != CalendarSourceKind.Local) == true
                || (await _documents.LoadAsync<List<CalendarEvent>>(CalendarEventsKey))?.Count > 0))
        {
            return true;
        }

        return parts.HasFlag(BackupParts.History) && await _activityLog.CountEntriesAsync() > 0;
    }

    /// <param name="overwrite">True: each chosen part becomes exactly what the backup has. False: the backup's items are
    /// added next to the current ones, and where both have a setting the current one stays.</param>
    public async Task<RestoreResult> RestoreAsync(BackupContent content, BackupParts parts, bool overwrite)
    {
        parts &= content.Parts;
        string? safetyBackup = overwrite ? await SaveSafetyBackupAsync() : null;
        var summary = new List<string>();
        bool needsRestart = false;

        if (parts.HasFlag(BackupParts.Profiles) && content.Profiles is not null)
        {
            ProfileDocument incoming = ProfileDocumentSerializer.Deserialize(content.Profiles);
            // A backup from before day plans has none of its own (it would get the starting plan): keep this one's.
            bool hasDayPlan = ProfileDocumentSerializer.HasDayPlan(content.Profiles);
            PendingChanges? pending = await _configuration.UpdateAsync(current =>
                !overwrite ? MergeProfiles(current, incoming)
                : hasDayPlan ? incoming
                : incoming with { DayPlan = current.DayPlan });
            summary.Add($"Profiles restored ({incoming.Profiles.Count} in the backup)"
                        + (pending is not null ? "; the looser parts take effect after the countdown." : "."));
        }

        if (parts.HasFlag(BackupParts.Settings) && content.Settings is not null)
        {
            int written = await RestoreSettingsAsync(content, overwrite);
            needsRestart |= written > 0;
            summary.Add($"Settings restored ({written} kept from the backup).");
        }

        if (parts.HasFlag(BackupParts.Calendars) && content.CalendarSources is not null)
        {
            await RestoreCalendarsAsync(content, overwrite);
            needsRestart = true;
            summary.Add($"Calendars restored ({content.CalendarSources.Count} source(s), {content.CalendarEvents?.Count ?? 0} in-app event(s)).");
        }

        if (parts.HasFlag(BackupParts.History) && content.History is not null)
        {
            int added = await _activityLog.ImportHistoryAsync(content.History, replace: overwrite);
            summary.Add($"History restored ({added} entries added).");
        }

        // ADR 71: the synced settings go through their services: an ordinary edit that syncs out (a looser part waits).
        if (parts.HasFlag(BackupParts.SyncedSettings) && content.Synced is not null && Synced is not null)
        {
            summary.AddRange(await RestoreSyncedAsync(content.Synced));
        }

        if (parts.HasFlag(BackupParts.DeviceSettings) && content.Device is not null)
        {
            if (content.Platform is string platform && platform != BackupKinds.PlatformPc)
            {
                summary.Add("This PC's settings weren't restored: the backup is from a phone.");
            }
            else
            {
                (int written, IReadOnlyList<string> notes) = await RestoreDeviceAsync(content, overwrite);
                needsRestart |= written > 0;
                summary.Add($"This PC's settings restored ({written} kept from the backup).");
                summary.AddRange(notes);
            }
        }

        if (parts.HasFlag(BackupParts.TaskLists) && content.Tasks is JsonElement tasks && Tasks is not null)
        {
            summary.Add(await Tasks.Import(tasks, overwrite));
        }

        if (parts.HasFlag(BackupParts.TaskLists) && content.Goals is JsonElement goals && Goals is not null)
        {
            summary.Add(await Goals.Import(goals, overwrite));
        }

        return new RestoreResult(needsRestart, safetyBackup, summary);
    }

    private async Task<BackupContent> CreateContentAsync(BackupParts parts, bool includeSecrets)
    {
        string? profiles = parts.HasFlag(BackupParts.Profiles) ? ProfileDocumentSerializer.Serialize(_configuration.Current) : null;

        Dictionary<string, JsonElement>? settings = null;
        string? googleClientSecret = null;
        if (parts.HasFlag(BackupParts.Settings))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string key in SettingsKeys.Where(key => key != CalendarSettingsKey))
            {
                if (await _documents.LoadAsync<JsonElement?>(key) is JsonElement value)
                {
                    settings[key] = value;
                }
            }

            if (await _documents.LoadAsync<CalendarSettings>(CalendarSettingsKey) is { } calendarSettings)
            {
                googleClientSecret = includeSecrets ? Unprotect(calendarSettings.ProtectedGoogleClientSecret) : null;
                settings[CalendarSettingsKey] = JsonSerializer.SerializeToElement(
                    calendarSettings with { ProtectedGoogleClientSecret = null }, SerializerOptions);
            }
        }

        List<BackupCalendarSource>? sources = null;
        List<CalendarEvent>? events = null;
        if (parts.HasFlag(BackupParts.Calendars))
        {
            // Without secrets, links and Google accounts can't work, so only the in-app calendar comes along.
            sources = ((await _documents.LoadAsync<List<CalendarSource>>(CalendarSourcesKey)) ?? new List<CalendarSource>())
                .Where(source => includeSecrets || source.Kind == CalendarSourceKind.Local)
                .Select(source => new BackupCalendarSource(source with { ProtectedSecret = null }, includeSecrets ? Unprotect(source.ProtectedSecret) : null))
                .ToList();
            events = (await _documents.LoadAsync<List<CalendarEvent>>(CalendarEventsKey)) ?? new List<CalendarEvent>();
        }

        ActivityHistoryExport? history = parts.HasFlag(BackupParts.History)
            ? await _activityLog.ExportHistoryAsync(includeProtected: includeSecrets)
            : null;

        Dictionary<string, JsonElement>? synced = parts.HasFlag(BackupParts.SyncedSettings) && Synced is not null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["sleep"] = Synced.Sleep(),
                ["daybar"] = Synced.DayBar(),
                ["goal"] = Synced.Goal(),
                ["guidance"] = Synced.Mode(),
            }
            : null;

        Dictionary<string, JsonElement>? device = null;
        if (parts.HasFlag(BackupParts.DeviceSettings))
        {
            device = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string key in (await _documents.ListKeysAsync()).Where(DeviceSettingsRegistry.IsDevice))
            {
                if (await _documents.LoadAsync<JsonElement?>(key) is JsonElement value)
                {
                    (device[key], string? secret) = WithoutSealedFields(key, value, includeSecrets);
                    googleClientSecret ??= secret;
                }
            }
        }

        JsonElement? tasks = parts.HasFlag(BackupParts.TaskLists) && Tasks is not null ? Tasks.Export(includeSecrets) : null;
        JsonElement? goals = parts.HasFlag(BackupParts.TaskLists) && Goals is not null ? Goals.Export(includeSecrets) : null;

        return new BackupContent(_time.GetUtcNow(), AppVersion, includeSecrets, profiles, settings, googleClientSecret, sources, events, history,
            Synced: synced, Device: device, Tasks: tasks, Goals: goals);
    }

    // Fields sealed to this PC (DPAPI) don't travel: the Google client secret goes in plain text only with a password (as in the
    // Settings part), and the After sunset place never (ADR 49).
    private (JsonElement Value, string? GoogleClientSecret) WithoutSealedFields(string key, JsonElement value, bool includeSecrets)
    {
        if (key == CalendarSettingsKey && value.Deserialize<CalendarSettings>(SerializerOptions) is { } calendar)
        {
            string? secret = includeSecrets ? Unprotect(calendar.ProtectedGoogleClientSecret) : null;
            return (JsonSerializer.SerializeToElement(calendar with { ProtectedGoogleClientSecret = null }, SerializerOptions), secret);
        }

        if ((key == PerModeSettings.ThemeKey || key == PerModeSettings.ThemeKey + ModeSettings.CompanionSuffix)
            && System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()) is System.Text.Json.Nodes.JsonObject theme)
        {
            theme.Remove("protectedLocation");
            theme.Remove("ProtectedLocation");
            return (JsonSerializer.SerializeToElement(theme, SerializerOptions), null);
        }

        // Round 2 R4: Deep work's waiting changes are state (goals.deepWork.pending); one left in an older goals.deepWork never travels.
        if (key == "goals.deepWork" && System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()) is System.Text.Json.Nodes.JsonObject deepWork)
        {
            foreach (string pending in new[] { "pendingDaily", "pendingWeekly", "pendingTrackTimeOnly" })
            {
                deepWork.Remove(pending);
            }

            return (JsonSerializer.SerializeToElement(deepWork, SerializerOptions), null);
        }

        return (value, null);
    }

    private async Task<IEnumerable<string>> RestoreSyncedAsync(IReadOnlyDictionary<string, JsonElement> synced)
    {
        var notes = new List<string> { "Synced settings restored; your other devices get them at the next sync." };
        var parts = new (string Key, Func<JsonElement, Task<string?>> Restore)[]
        {
            ("sleep", Synced!.RestoreSleep),
            ("daybar", Synced.RestoreDayBar),
            ("goal", Synced.RestoreGoal),
            ("guidance", Synced.RestoreMode),
        };
        foreach ((string key, Func<JsonElement, Task<string?>> restore) in parts)
        {
            if (synced.TryGetValue(key, out JsonElement value) && await restore(value) is string note)
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    private async Task<(int Written, IReadOnlyList<string> Notes)> RestoreDeviceAsync(BackupContent content, bool overwrite)
    {
        int written = 0;
        var notes = new List<string>();
        // Only this PC's device settings are written, whatever else the file contains.
        // The first goals build put the sealed goal lists here: still taken in through their restorers (never written raw).
        foreach ((string key, JsonElement value) in content.Device!.Where(entry =>
                     DeviceSettingsRegistry.IsDevice(entry.Key) || (DeviceSettingsRegistry.GoalKeys.Contains(entry.Key) && DeviceRestorers.ContainsKey(entry.Key))))
        {
            if (!overwrite && await _documents.LoadAsync<JsonElement?>(key) is not null)
            {
                continue;
            }

            if (DeviceRestorers.TryGetValue(key, out Func<JsonElement, Task<string?>>? restore))
            {
                if (await restore(value) is string note)
                {
                    notes.Add(note);
                }

                continue;
            }

            if (key == CalendarSettingsKey)
            {
                CalendarSettings settings = value.Deserialize<CalendarSettings>(SerializerOptions) ?? CalendarSettings.Default;
                await _documents.SaveAsync(key, settings with { ProtectedGoogleClientSecret = Protect(content.GoogleClientSecret) });
            }
            else
            {
                await _documents.SaveAsync(key, value);
            }

            written++;
        }

        return (written, notes);
    }

    /// <summary>
    /// Adds the backup's profiles that aren't here yet (by id) and its one-off days on dates that have none here (when
    /// their categories exist here). Everything else (the day plan's templates, Pomodoro, escalation, locks) stays as it is.
    /// </summary>
    private static ProfileDocument MergeProfiles(ProfileDocument current, ProfileDocument incoming)
    {
        HashSet<Guid> profileIds = current.Profiles.Select(profile => profile.Id).ToHashSet();
        HashSet<string> names = current.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<FocusProfile> added = incoming.Profiles
            .Where(profile => !profileIds.Contains(profile.Id))
            .Select(profile => names.Contains(profile.Name) ? profile with { Name = profile.Name + " (restored)" } : profile)
            .ToList();
        profileIds.UnionWith(added.Select(profile => profile.Id));

        HashSet<DateOnly> plannedDates = current.DayPlan.OneOffDays.Select(day => day.Date).ToHashSet();
        HashSet<Guid> categoryIds = current.DayPlan.Categories.Select(category => category.Id).ToHashSet();
        List<OneOffDay> addedDays = incoming.DayPlan.OneOffDays
            .Where(day => !plannedDates.Contains(day.Date) && day.Template.Parts.All(part => categoryIds.Contains(part.CategoryId)))
            .ToList();
        return current with
        {
            Profiles = current.Profiles.Concat(added).ToList(),
            DayPlan = current.DayPlan with
            {
                OneOffDays = current.DayPlan.OneOffDays.Concat(addedDays).OrderBy(day => day.Date).ToList(),
            },
        };
    }

    private async Task<int> RestoreSettingsAsync(BackupContent content, bool overwrite)
    {
        int written = 0;
        // Only known keys are written, whatever else the file contains.
        foreach (string key in SettingsKeys)
        {
            if (!content.Settings!.TryGetValue(key, out JsonElement value)
                || (!overwrite && await _documents.LoadAsync<JsonElement?>(key) is not null))
            {
                continue;
            }

            if (key == CalendarSettingsKey)
            {
                CalendarSettings settings = value.Deserialize<CalendarSettings>(SerializerOptions) ?? CalendarSettings.Default;
                await _documents.SaveAsync(key, settings with { ProtectedGoogleClientSecret = Protect(content.GoogleClientSecret) });
            }
            else
            {
                await _documents.SaveAsync(key, value);
            }

            written++;
        }

        return written;
    }

    private async Task RestoreCalendarsAsync(BackupContent content, bool overwrite)
    {
        List<CalendarSource> incoming = content.CalendarSources!
            .Select(backup => backup.Source with { ProtectedSecret = Protect(backup.Secret) })
            .ToList();
        List<CalendarEvent> incomingEvents = content.CalendarEvents?.ToList() ?? new List<CalendarEvent>();

        if (overwrite)
        {
            await _documents.SaveAsync(CalendarSourcesKey, incoming);
            await _documents.SaveAsync(CalendarEventsKey, incomingEvents);
            return;
        }

        List<CalendarSource> sources = (await _documents.LoadAsync<List<CalendarSource>>(CalendarSourcesKey)) ?? new List<CalendarSource>();
        HashSet<string> sourceIds = sources.Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        bool hasLocal = sources.Any(source => source.Kind == CalendarSourceKind.Local);
        sources.AddRange(incoming.Where(source => !sourceIds.Contains(source.Id) && !(hasLocal && source.Kind == CalendarSourceKind.Local)));
        await _documents.SaveAsync(CalendarSourcesKey, sources);

        List<CalendarEvent> events = (await _documents.LoadAsync<List<CalendarEvent>>(CalendarEventsKey)) ?? new List<CalendarEvent>();
        HashSet<string> eventIds = events.Select(calendarEvent => calendarEvent.Id).ToHashSet(StringComparer.Ordinal);
        events.AddRange(incomingEvents.Where(calendarEvent => !eventIds.Contains(calendarEvent.Id)));
        await _documents.SaveAsync(CalendarEventsKey, events.OrderBy(calendarEvent => calendarEvent.LocalStart).ToList());
    }

    /// <summary>Everything as it is now, for this computer only, before an overwrite.</summary>
    private async Task<string> SaveSafetyBackupAsync()
    {
        BackupContent content = await CreateContentAsync(BackupParts.All, includeSecrets: true);
        string file = BackupEnvelope.Seal(JsonSerializer.Serialize(content, SerializerOptions), BackupProtection.ThisComputer, protector: _protector);
        Directory.CreateDirectory(_backupFolder);
        string stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(_backupFolder, $"before-restore-{stamp}{BackupEnvelope.FileExtension}");
        await File.WriteAllTextAsync(path, file);
        return path;
    }

    private string? Unprotect(string? protectedBase64)
    {
        if (protectedBase64 is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(protectedBase64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string? Protect(string? secret)
    {
        return secret is null ? null : Convert.ToBase64String(_protector.Protect(secret));
    }
}
