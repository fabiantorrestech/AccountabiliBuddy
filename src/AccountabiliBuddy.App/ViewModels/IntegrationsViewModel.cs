using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Integrations tab: living alongside other blockers.
/// <list type="bullet">
/// <item>Cold Turkey: detection, coexistence notes, and the sites it was seen blocking.</item>
/// <item>Import a list pasted from any blocker into a profile.</item>
/// </list>
/// Block rules can be added at any time (they only tighten a profile); allow rules are refused during focus,
/// like every other way of loosening a profile mid-session.
/// </summary>
public sealed partial class IntegrationsViewModel : ObservableObject
{
    private const string ImportNote = "Imported from a block list";
    private const string SightingNote = "Seen blocked by Cold Turkey";

    private readonly IColdTurkeyDetector _detector;
    private readonly ColdTurkeySightingsService _sightings;
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public IntegrationsViewModel(
        IColdTurkeyDetector detector,
        ColdTurkeySightingsService sightings,
        ConfigurationService configuration,
        SessionController sessions)
    {
        _detector = detector;
        _sightings = sightings;
        _configuration = configuration;
        _sessions = sessions;

        ColdTurkeySummary = ColdTurkeyDetails = ImportText = string.Empty;

        _sightings.Changed += (_, _) => RefreshSightings();
        _configuration.Changed += (_, _) => RefreshProfiles();

        RefreshProfiles();
        RefreshSightings();
        DetectColdTurkey();
    }

    public ObservableCollection<ProfileOption> ProfileOptions { get; } = new();

    public ObservableCollection<SightingRowViewModel> Sightings { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial ProfileOption? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial bool IsColdTurkeyInstalled { get; set; }

    [ObservableProperty]
    public partial string ColdTurkeySummary { get; set; }

    [ObservableProperty]
    public partial string ColdTurkeyDetails { get; set; }

    [ObservableProperty]
    public partial bool HasNoSightings { get; set; }

    [ObservableProperty]
    public partial string? SightingsNote { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial string ImportText { get; set; }

    /// <summary>False = import as Block rules; true = as Allow rules (e.g. a blocker's exception list).</summary>
    [ObservableProperty]
    public partial bool ImportAsAllow { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    private bool CanImport => SelectedProfile is not null && !string.IsNullOrWhiteSpace(ImportText);

    [RelayCommand]
    private void DetectColdTurkey()
    {
        ColdTurkeyStatus status = _detector.Detect();
        IsColdTurkeyInstalled = status.IsInstalled;

        if (!status.IsInstalled)
        {
            ColdTurkeySummary = "Cold Turkey Blocker is not installed.";
            ColdTurkeyDetails = string.Empty;
            return;
        }

        ColdTurkeySummary = $"Cold Turkey Blocker {status.Version ?? "(unknown version)"} · "
            + (status.IsRunning ? "running" : "not running");

        string browsers = status.BrowsersWithExtensionHost.Count == 0
            ? "no browsers"
            : string.Join(", ", status.BrowsersWithExtensionHost);
        ColdTurkeyDetails = string.Join(Environment.NewLine,
            $"Installed in {status.InstallFolder}; its browser extension host is registered for {browsers}.",
            "Coexistence is automatic: Cold Turkey's block pages (and any other extension's pages) never count as distracting, "
                + "and AccountabiliBuddy never redirects a tab Cold Turkey already blocked.",
            "Cold Turkey's own settings are never read or changed.");
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        RuleVerdict verdict = ImportAsAllow ? RuleVerdict.Allow : RuleVerdict.Block;
        if (verdict == RuleVerdict.Allow && _sessions.IsEnforcing)
        {
            StatusMessage = "Allow rules can't be imported during a focus session (they would loosen it).";
            return;
        }

        BlockListParseResult parsed = BlockListParser.Parse(ImportText, verdict, ImportNote);
        int added = await _configuration.AddRulesAsync(SelectedProfile!.Id, parsed.Rules);

        int duplicates = parsed.Rules.Count - added;
        StatusMessage = $"Added {added} rule(s) to {SelectedProfile.Name}."
            + (verdict == RuleVerdict.Allow && added > 0 ? " Allow rules are looser, so they take effect after the countdown (Profiles tab)." : string.Empty)
            + (duplicates > 0 ? $" {duplicates} were already there." : string.Empty)
            + (parsed.SkippedLines.Count > 0 ? $" Skipped {parsed.SkippedLines.Count} line(s) that weren't a site or app: {string.Join(", ", parsed.SkippedLines.Take(3))}" : string.Empty);

        if (added > 0)
        {
            ImportText = string.Empty;
        }
    }

    private async void AddSighting(SightingRowViewModel row)
    {
        if (SelectedProfile is not { } profile)
        {
            StatusMessage = "Pick a profile first.";
            return;
        }

        var rule = new Rule(Guid.NewGuid(), MatchTarget.Url, MatchKind.Domain, row.Site, RuleVerdict.Block, SightingNote);
        int added = await _configuration.AddRulesAsync(profile.Id, new[] { rule });
        StatusMessage = added > 0 ? $"{row.Site} is now blocked in {profile.Name}." : $"{profile.Name} already blocks {row.Site}.";
    }

    private void ForgetSighting(SightingRowViewModel row)
    {
        _sightings.Forget(row.Site);
    }

    private void RefreshProfiles()
    {
        SelectedProfile = ViewModels.ProfileOptions.Refresh(ProfileOptions, _configuration.Current.Profiles, SelectedProfile?.Id);
    }

    private void RefreshSightings()
    {
        Sightings.Clear();
        foreach (BlockedSiteSighting sighting in _sightings.Sightings)
        {
            Sightings.Add(new SightingRowViewModel(sighting, AddSighting, ForgetSighting));
        }

        HasNoSightings = Sightings.Count == 0;
        SightingsNote = _sightings.SawBlockPageWithoutSite
            ? "Cold Turkey's block page was seen, but its address doesn't say which site it blocked, so it can't be listed here. Use \"Import a list\" instead."
            : null;
    }
}
