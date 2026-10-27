using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Loads, saves and announces changes to <see cref="SyncSettings"/>. Use from the UI thread only.</summary>
public sealed class SyncSettingsService
{
    private const string DocumentKey = "sync.settings";

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;

    public SyncSettingsService(SqliteJsonDocumentStore store, IDataProtector protector)
    {
        _store = store;
        _protector = protector;
    }

    public SyncSettings Current { get; private set; } = SyncSettings.Default;

    public event EventHandler<SyncSettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<SyncSettings>(DocumentKey)).GetAwaiter().GetResult() ?? SyncSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = SyncSettings.Default;
        }
    }

    public async Task SaveAsync(SyncSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }

    /// <summary>The relay's admission key (DPAPI, ADR 46); null when none is saved or it can't be unprotected (copied from another user or PC).</summary>
    public string? ReadRelayAdmission()
    {
        string? stored = Current.RelayAdmissionProtected;
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(stored));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The value for <see cref="SyncSettings.RelayAdmissionProtected"/> that holds <paramref name="admissionKey"/>: the
    /// stored one when it already holds the same key (so saving without a change doesn't look like a change), else a
    /// newly protected one; null for no key.
    /// </summary>
    public string? ProtectRelayAdmission(string? admissionKey)
    {
        if (string.IsNullOrWhiteSpace(admissionKey))
        {
            return null;
        }

        string key = admissionKey.Trim();
        return ReadRelayAdmission() == key ? Current.RelayAdmissionProtected : Convert.ToBase64String(_protector.Protect(key));
    }
}
