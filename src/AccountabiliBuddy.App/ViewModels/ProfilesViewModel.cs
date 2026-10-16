using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Profiles tab. Edits are kept in memory until Save, so a half-written rule never takes effect.
/// Editing is locked during a focus phase so rules cannot be loosened mid-session.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public ProfilesViewModel(ConfigurationService configuration, SessionController sessions)
    {
        _configuration = configuration;
        _sessions = sessions;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;

        LoadFromConfiguration();
    }

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

        int scheduledBlocks = _configuration.Current.Schedule.Blocks.Count(block => block.ProfileId == profile.Id);
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

        await _configuration.UpdateAsync(document => document with { Profiles = built });
        StatusMessage = _configuration.IsReadOnly ? "Not saved: settings are read-only (see banner)." : "Saved.";
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
        foreach (FocusProfile profile in _configuration.Current.Profiles)
        {
            Profiles.Add(ProfileEditorViewModel.FromProfile(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(editor => editor.Id == selectedId) ?? Profiles.FirstOrDefault();
    }
}
