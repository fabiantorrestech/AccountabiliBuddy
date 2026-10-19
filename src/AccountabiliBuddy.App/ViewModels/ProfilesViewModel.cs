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

    // Every rule id this tab already has an editor for, so rules added elsewhere can be told apart.
    private readonly HashSet<Guid> _knownRuleIds = new();

    public ProfilesViewModel(ConfigurationService configuration, SessionController sessions, PendingChangesViewModel pendingChanges)
    {
        _configuration = configuration;
        _sessions = sessions;
        PendingChanges = pendingChanges;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        _configuration.Changed += OnConfigurationChanged;
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
        PendingChanges? waiting = await _configuration.UpdateAsync(document => document with { Profiles = built });
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

    private void AddEditor(ProfileEditorViewModel editor)
    {
        Profiles.Add(editor);
        SelectedProfile = editor;
        StatusMessage = "Press Save to keep the new profile.";
    }

    private void LoadFromConfiguration()
    {
        Guid? selectedId = SelectedProfile?.Id;

        Profiles.Clear();
        _knownRuleIds.Clear();
        foreach (FocusProfile profile in _configuration.Editing.Profiles)
        {
            Profiles.Add(ProfileEditorViewModel.FromProfile(profile));
            _knownRuleIds.UnionWith(profile.Rules.Select(rule => rule.Id));
        }

        SelectedProfile = Profiles.FirstOrDefault(editor => editor.Id == selectedId) ?? Profiles.FirstOrDefault();
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
