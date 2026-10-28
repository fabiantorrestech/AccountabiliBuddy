using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Google setup guide: one step at a time, with what to click, a button that opens the right Google Cloud
/// Console page, values to copy, and at the end the client ID and secret and a connection test. Progress is kept,
/// so the guide can be closed and picked up later. A failed test points to the step that most likely fixes it.
/// </summary>
public sealed partial class GoogleSetupViewModel : ObservableObject
{
    private readonly Action<string, string?> _saveClient;
    private readonly Func<Task<string?>> _connectCalendar;
    private readonly Func<Uri, bool> _openBrowser;
    private readonly HashSet<string> _done;

    public GoogleSetupViewModel(
        GoogleSetupProgress progress,
        string? clientId,
        bool hasClientSecret,
        Action<string, string?> saveClient,
        Func<Task<string?>> connectCalendar,
        Func<Uri, bool> openBrowser)
    {
        _saveClient = saveClient;
        _connectCalendar = connectCalendar;
        _openBrowser = openBrowser;
        _done = new HashSet<string>(progress.DoneStepIds);

        ClientId = clientId ?? string.Empty;
        ClientSecret = string.Empty;
        SecretHint = hasClientSecret ? "Client secret (saved; paste to replace)" : "Client secret";
        IncludeSleep = progress.IncludeSleep;
        BuildSteps();
    }

    /// <summary>Raised whenever a step is marked done or the sleep choice changes.</summary>
    public event EventHandler<GoogleSetupProgress>? ProgressChanged;

    public ObservableCollection<GoogleSetupStepViewModel> Steps { get; } = new();

    /// <summary>Also set up sleep tracking (adds the Google Health API and the sleep scope).</summary>
    [ObservableProperty]
    public partial bool IncludeSleep { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(PositionText), nameof(Instructions), nameof(CopyValues), nameof(HasPage),
        nameof(IsClientStep), nameof(IsTestStep), nameof(IsFirst), nameof(NextLabel))]
    public partial GoogleSetupStepViewModel? Current { get; set; }

    public string Heading => Current?.Step.Title ?? string.Empty;

    public string PositionText => Current is null ? string.Empty : $"Step {Current.Number} of {Steps.Count}";

    public IReadOnlyList<string> Instructions => Current?.Step.Instructions ?? Array.Empty<string>();

    public IReadOnlyList<GoogleSetupCopyValue> CopyValues => Current?.Step.CopyValues ?? Array.Empty<GoogleSetupCopyValue>();

    public bool HasPage => Current?.Step.PageUrl is not null;

    public bool IsClientStep => Current?.Step.Id == GoogleSetupSteps.PasteClientId;

    public bool IsTestStep => Current?.Step.Id == GoogleSetupSteps.TestId;

    public bool IsFirst => Current is null || Steps.IndexOf(Current) == 0;

    public string NextLabel => Current is not null && Steps.IndexOf(Current) == Steps.Count - 1 ? "✓ Done" : "✓ Done, next";

    public string DoneSummary => $"{Steps.Count(step => step.IsDone)} of {Steps.Count} done";

    [ObservableProperty]
    public partial string ClientId { get; set; }

    [ObservableProperty]
    public partial string ClientSecret { get; set; }

    public string SecretHint { get; private set; }

    /// <summary>What happened on this step (saved, connected, or what went wrong and where to fix it).</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private void OpenPage()
    {
        if (Current?.Step.PageUrl is string url && !_openBrowser(new Uri(url)))
        {
            Message = $"Couldn't open the browser. Go to {url}";
        }
    }

    [RelayCommand]
    private void Select(GoogleSetupStepViewModel step)
    {
        ShowStep(step);
    }

    [RelayCommand]
    private void Back()
    {
        if (Current is not null && Steps.IndexOf(Current) > 0)
        {
            ShowStep(Steps[Steps.IndexOf(Current) - 1]);
        }
    }

    /// <summary>Marks the step done and moves to the next one that isn't done yet.</summary>
    [RelayCommand]
    private void Next()
    {
        if (Current is null)
        {
            return;
        }

        MarkDone(Current);
        int index = Steps.IndexOf(Current);
        GoogleSetupStepViewModel? next = Steps.Skip(index + 1).FirstOrDefault(step => !step.IsDone)
            ?? Steps.ElementAtOrDefault(index + 1);
        if (next is not null)
        {
            ShowStep(next);
        }
        else
        {
            Message = "All done. You can close this window.";
        }
    }

    [RelayCommand]
    private void SaveClient()
    {
        string id = ClientId.Trim();
        if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            Message = "That doesn't look like a client ID: it ends with .apps.googleusercontent.com.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ClientSecret) && SecretHint == "Client secret")
        {
            Message = "Paste the client secret too.";
            return;
        }

        _saveClient(id, ClientSecret);
        ClientSecret = string.Empty;
        SecretHint = "Client secret (saved; paste to replace)";
        OnPropertyChanged(nameof(SecretHint));
        Next();
        Message = "Saved. Now connect to test it.";
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        Message = "Finish signing in to Google in your browser…";
        try
        {
            string? error = await _connectCalendar();
            if (error is null)
            {
                Message = IncludeSleep
                    ? "Google Calendar connected. Sleep data is tested once the Sleep tab arrives (this project is ready for it)."
                    : "Google Calendar connected. Your calendars are in Plan, under Calendar.";
                if (Current is not null)
                {
                    MarkDone(Current);
                }

                return;
            }

            string? fixStep = GoogleSetupSteps.StepForError(error);
            GoogleSetupStepViewModel? fix = Steps.FirstOrDefault(step => step.Step.Id == fixStep);
            Message = fix is null ? error : $"{error}\nThis is usually fixed in step {fix.Number}: {fix.Step.Title}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect => !IsBusy;

    partial void OnIncludeSleepChanged(bool value)
    {
        if (Steps.Count > 0)
        {
            string? currentId = Current?.Step.Id;
            BuildSteps(currentId);
            RaiseProgress();
        }
    }

    private void BuildSteps(string? showId = null)
    {
        Steps.Clear();
        int number = 1;
        foreach (GoogleSetupStep step in GoogleSetupSteps.For(IncludeSleep))
        {
            Steps.Add(new GoogleSetupStepViewModel(step, number++, _done.Contains(step.Id)));
        }

        GoogleSetupStepViewModel first = Steps.FirstOrDefault(step => step.Step.Id == showId)
            ?? Steps.FirstOrDefault(step => !step.IsDone)
            ?? Steps[0];
        ShowStep(first);
        OnPropertyChanged(nameof(DoneSummary));
    }

    private void ShowStep(GoogleSetupStepViewModel step)
    {
        if (Current is not null)
        {
            Current.IsCurrent = false;
        }

        step.IsCurrent = true;
        Current = step;
        Message = null;
    }

    private void MarkDone(GoogleSetupStepViewModel step)
    {
        step.IsDone = true;
        _done.Add(step.Step.Id);
        OnPropertyChanged(nameof(DoneSummary));
        RaiseProgress();
    }

    private void RaiseProgress()
    {
        ProgressChanged?.Invoke(this, new GoogleSetupProgress(_done.ToList(), IncludeSleep));
    }
}
