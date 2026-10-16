using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Single owner of the in-memory <see cref="ProfileDocument"/>. Every change goes through
/// <see cref="UpdateAsync"/>, which notifies listeners and persists the result.
/// Use from the UI thread only.
/// </summary>
public sealed class ConfigurationService
{
    private readonly IProfileStore _store;

    public ConfigurationService(IProfileStore store)
    {
        _store = store;
    }

    public ProfileDocument Current { get; private set; } = ProfileDocument.CreateDefault();

    /// <summary>Set when saved settings could not be read. Saving is then disabled so nothing is overwritten.</summary>
    public string? LoadError { get; private set; }

    public bool IsReadOnly => LoadError is not null;

    public event EventHandler<ProfileDocument>? Changed;

    /// <summary>
    /// Loads synchronously during startup, before any window exists.
    /// Runs on the thread pool so blocking here cannot deadlock the UI thread.
    /// </summary>
    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync()).GetAwaiter().GetResult();
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            LoadError = $"{ex.Message} Changes will not be saved.";
        }
        catch (JsonException ex)
        {
            LoadError = $"Saved settings could not be read ({ex.Message}). Changes will not be saved.";
        }
        catch (SqliteException ex)
        {
            LoadError = $"The settings database could not be opened ({ex.Message}). Changes will not be saved.";
        }
    }

    /// <summary>
    /// Appends rules to one profile, skipping any that duplicate an existing rule (same target, kind and pattern).
    /// </summary>
    /// <returns>How many rules were added.</returns>
    public async Task<int> AddRulesAsync(Guid profileId, IEnumerable<Rule> rules)
    {
        FocusProfile? profile = Current.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null)
        {
            return 0;
        }

        var existing = new HashSet<string>(profile.Rules.Select(RuleKey), StringComparer.OrdinalIgnoreCase);
        List<Rule> added = rules.Where(rule => existing.Add(RuleKey(rule))).ToList();
        if (added.Count == 0)
        {
            return 0;
        }

        await UpdateAsync(document => document with
        {
            Profiles = document.Profiles
                .Select(candidate => candidate.Id == profileId
                    ? candidate with { Rules = candidate.Rules.Concat(added).ToList() }
                    : candidate)
                .ToList(),
        });
        return added.Count;
    }

    public async Task UpdateAsync(Func<ProfileDocument, ProfileDocument> change)
    {
        Current = change(Current);
        Changed?.Invoke(this, Current);

        if (!IsReadOnly)
        {
            await _store.SaveAsync(Current);
        }
    }

    private static string RuleKey(Rule rule)
    {
        return $"{rule.Target}|{rule.Kind}|{rule.Pattern.Trim()}";
    }
}
