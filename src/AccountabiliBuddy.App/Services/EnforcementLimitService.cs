using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// How far escalation may go (ADR 22's Work computer type: notice and overlay, never minimizing or redirecting a work
/// window), app-owned ("enforcement.limit"). Use from the UI thread only.
/// </summary>
public sealed class EnforcementLimitService
{
    private const string DocumentKey = "enforcement.limit";

    private readonly SqliteJsonDocumentStore _store;

    public EnforcementLimitService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary><see cref="EscalationLevel.Enforce"/> (minimize and redirect allowed, the default) or less.</summary>
    public EscalationLevel MaxLevel { get; private set; } = EscalationLevel.Enforce;

    public event EventHandler<EscalationLevel>? Changed;

    public void LoadBlocking()
    {
        try
        {
            MaxLevel = Task.Run(() => _store.LoadAsync<Limit>(DocumentKey)).GetAwaiter().GetResult()?.MaxLevel ?? EscalationLevel.Enforce;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            MaxLevel = EscalationLevel.Enforce;
        }
    }

    public async Task SaveAsync(EscalationLevel maxLevel)
    {
        MaxLevel = maxLevel;
        Changed?.Invoke(this, maxLevel);
        await _store.SaveAsync(DocumentKey, new Limit(maxLevel));
    }

    private sealed record Limit(EscalationLevel MaxLevel);
}
