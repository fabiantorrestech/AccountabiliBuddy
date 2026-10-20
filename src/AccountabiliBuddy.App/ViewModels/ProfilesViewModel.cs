using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Profiles tab. Edits are kept in memory until Save, so a half-written rule never takes effect.
/// Saving applies stricter changes at once; looser ones count down first and the tab shows them as saved
/// (see <see cref="PendingChangesViewModel"/>). Editing is locked during a focus phase.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly OverlaySettingsService _overlays;
    private readonly ColdTurkeyService _coldTurkey;

    // Every rule id this tab already has an editor for, so rules added elsewhere can be told apart.
    private readonly HashSet<Guid> _knownRuleIds = new();

    // Every saved profile this tab has seen, so a profile added elsewhere can be told from one deleted here.
    private readonly HashSet<Guid> _knownProfileIds = new();

    public ProfilesViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        PendingChangesViewModel pendingChanges,
        OverlaySettingsService overlays,
        ColdTurkeyService coldTurkey)
    {
        _configuration = configuration;
        _sessions = sessions;
        _overlays = overlays;
        _coldTurkey = coldTurkey;
        PendingChanges = pendingChanges;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        _configuration.Changed += OnConfigurationChanged;
        _overlays.Changed += OnOverlayChanged;
        _coldTurkey.Changed += (_, _) => ShowColdTurkeyBlocks();
        _configuration.PendingCancelled += (_, _) =>
        {
            LoadFromConfiguration();
            StatusMessage = "Looser changes cancelled.";
        };

        LoadFromConfiguration();
    }

    public PendingChangesViewModel PendingChanges { get; }

    public ObservableCollection<ProfileEditorViewModel> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    public partial ProfileEditorViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddExampleProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    public partial bool IsLocked { get; set; }

    private bool CanEdit => !IsLocked;

    private bool CanDelete => !IsLocked && SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddProfile()
    {
        AddEditor(ProfileEditorViewModel.CreateNew());
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddExampleProfile()
    {
        AddEditor(ProfileEditorViewModel.FromProfile(ExampleProfiles.Coding()));
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteProfile()
    {
        ProfileEditorViewModel profile = SelectedProfile!;

        int scheduledBlocks = _configuration.Editing.Schedule.Blocks.Count(block => block.ProfileId == profile.Id)
            + _configuration.Editing.Schedule.OneOffBlocks.Count(block => block.ProfileId == profile.Id);
        if (scheduledBlocks > 0)
        {
            StatusMessage = $"'{profile.Name}' is used by {scheduledBlocks} schedule block(s). Remove those first.";
            return;
        }

        if (_sessions.State.ProfileId == profile.Id)
        {
            StatusMessage = $"'{profile.Name}' is used by the running session.";
            return;
        }

        Profiles.Remove(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        StatusMessage = "Profile removed. Press Save to keep this change.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        var built = new List<FocusProfile>();
        foreach (ProfileEditorViewModel editor in Profiles)
        {
            if (!editor.TryBuild(out FocusProfile? profile, out string? error))
            {
                SelectedProfile = editor;
                StatusMessage = $"Not saved: {error}.";
                return;
            }

            built.Add(profile);
        }

        _knownRuleIds.UnionWith(built.SelectMany(profile => profile.Rules).Select(rule => rule.Id));
        _knownProfileIds.UnionWith(built.Select(profile => profile.Id));
        PendingChanges? waiting = await _configuration.UpdateAsync(document => document with { Profiles = built });
        if (!_configuration.IsReadOnly)
        {
            // Display only and this computer's own, so it applies at once even when the profiles wait.
            await _overlays.SaveProfilesAsync(Profiles.ToDictionary(editor => editor.Id, editor => editor.ToOverlay()));
            await _coldTurkey.SaveExpectedAsync(Profiles.ToDictionary(editor => editor.Id, editor => editor.ToExpectedBlocks()));
        }

        StatusMessage = _configuration.IsReadOnly
            ? "Not saved: settings are read-only (see banner)."
            : waiting is null ? "Saved." : "Saved. Looser changes wait for the countdown above.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Revert()
    {
        LoadFromConfiguration();
        StatusMessage = "Unsaved changes discarded.";
    }

    /// <summary>Shows this profile's editor (e.g. after clicking its block on the day bar).</summary>
    public void Select(Guid profileId)
    {
        SelectedProfile = Profiles.FirstOrDefault(editor => editor.Id == profileId) ?? SelectedProfile;
    }

    private void AddEditor(ProfileEditorViewModel editor)
    {
        editor.ShowColdTurkeyBlocks(ColdTurkeyBlockNames(), Array.Empty<string>());
        Profiles.Add(editor);
        SelectedProfile = editor;
        StatusMessage = "Press Save to keep the new profile.";
    }

    private void LoadFromConfiguration()
    {
        Guid? selectedId = SelectedProfile?.Id;

        Profiles.Clear();
        _knownRuleIds.Clear();
        _knownProfileIds.Clear();
        foreach (FocusProfile profile in _configuration.Editing.Profiles)
        {
            _knownProfileIds.Add(profile.Id);
            Profiles.Add(CreateEditor(profile));
            _knownRuleIds.UnionWith(profile.Rules.Select(rule => rule.Id));
        }

        SelectedProfile = Profiles.FirstOrDefault(editor => editor.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    /// <summary>An editor for a saved profile, with this computer's own choices for it (timer, Cold Turkey blocks).</summary>
    private ProfileEditorViewModel CreateEditor(FocusProfile profile)
    {
        ProfileEditorViewModel editor = ProfileEditorViewModel.FromProfile(profile, _overlays.Current.SavedFor(profile.Id));
        editor.ShowColdTurkeyBlocks(ColdTurkeyBlockNames(), _coldTurkey.ExpectedFor(profile.Id));
        return editor;
    }

    private IEnumerable<string> ColdTurkeyBlockNames()
    {
        return _coldTurkey.Blocks?.Blocks.Select(block => block.Name) ?? Enumerable.Empty<string>();
    }

    /// <summary>Cold Turkey's blocks changed (one added or renamed there): list them again, keeping unsaved ticks.</summary>
    private void ShowColdTurkeyBlocks()
    {
        foreach (ProfileEditorViewModel editor in Profiles)
        {
            editor.ShowColdTurkeyBlocks(ColdTurkeyBlockNames(), editor.ToExpectedBlocks());
        }
    }

    /// <summary>
    /// The timer choices can also be changed in the Settings tab. Show those, so saving this tab doesn't put back
    /// the old ones. Profiles not saved yet keep what was picked here.
    /// </summary>
    private void OnOverlayChanged(object? sender, OverlaySettings settings)
    {
        foreach (ProfileEditorViewModel editor in Profiles)
        {
            if (settings.Profiles.TryGetValue(editor.Id, out ProfileOverlay? overlay))
            {
                editor.ShowOverlay(overlay);
            }
        }
    }

    /// <summary>
    /// Rules can be added outside this tab (the classify prompt, the Integrations tab). Merge those in
    /// without discarding edits that have not been saved yet.
    /// </summary>
    private void OnConfigurationChanged(object? sender, ProfileDocument current)
    {
        int added = 0;
        foreach (FocusProfile profile in _configuration.Editing.Profiles)
        {
            ProfileEditorViewModel? editor = Profiles.FirstOrDefault(candidate => candidate.Id == profile.Id);
            if (editor is null && _knownProfileIds.Add(profile.Id))
            {
                // A whole profile added elsewhere (the Session tab's example profile).
                Profiles.Add(CreateEditor(profile));
                _knownRuleIds.UnionWith(profile.Rules.Select(rule => rule.Id));
                SelectedProfile ??= Profiles[^1];
                continue;
            }

            foreach (Rule rule in profile.Rules.Where(rule => _knownRuleIds.Add(rule.Id)))
            {
                editor?.AddSavedRule(rule);
                added++;
            }
        }

        if (added > 0)
        {
            StatusMessage = added == 1
                ? "A rule was added from the classify prompt or the Integrations tab."
                : $"{added} rules were added from the classify prompt or the Integrations tab.";
        }
    }
}
