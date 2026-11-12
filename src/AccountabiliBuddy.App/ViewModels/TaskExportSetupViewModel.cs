using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's page for task reminders in Google Calendar (ADR 78) on an existing install: the app now needs one more OK from
/// Google to add events to its own "AB Reminders" calendar. Google Calendar (Allow…, <see cref="TaskCalendarExportService.AllowExportAsync"/>)
/// or Nowhere ("Add new reminders to", <see cref="TaskReminderSettingsService.UpdateAsync"/>). Set up = allowed, or Nowhere chosen
/// (<see cref="IsSetUp"/>). Use from the UI thread only.
/// </summary>
public sealed partial class TaskExportSetupViewModel : ObservableObject
{
    private readonly TaskCalendarExportService _export;
    private readonly TaskReminderSettingsService _reminders;

    // True while the page reads the services: the properties' change handlers then save nothing.
    private bool _loading;

    public TaskExportSetupViewModel(TaskCalendarExportService export, TaskReminderSettingsService reminders)
    {
        _export = export;
        _reminders = reminders;
        AccountText = StatusText = string.Empty;
        _export.StatusChanged += (_, _) => Dispatcher.UIThread.Post(Load);
        _reminders.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        Load();
    }

    /// <summary>Opens Google's consent page in the browser (Allow); set by the app.</summary>
    public Func<Uri, bool>? OpenBrowser { get; init; }

    /// <summary>Export is allowed for the connected account, or the user chose Nowhere.</summary>
    public bool IsSetUp => _export.IsAllowed || _reminders.Current.NewRemindersTo == TaskCalendarDestination.Nowhere;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNowhere))]
    public partial bool IsGoogle { get; set; }

    public bool IsNowhere
    {
        get => !IsGoogle;
        set
        {
            if (value)
            {
                IsGoogle = false;
            }
        }
    }

    /// <summary>"Allowed: reminders go to “AB Reminders” in Google · name@example.com", or what still needs doing.</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; }

    [ObservableProperty]
    public partial bool HasAccount { get; set; }

    /// <summary>What the last Allow did or why it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => StatusText.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand))]
    public partial bool IsAllowing { get; set; }

    public void Load()
    {
        _loading = true;
        try
        {
            IsGoogle = _reminders.Current.NewRemindersTo == TaskCalendarDestination.GoogleCalendar;
            string? account = _export.ExportAccountName;
            HasAccount = account is not null;
            AccountText = account is null
                ? "Connect a Google account first (Settings › Connections), or pick Nowhere."
                : _export.IsAllowed
                    ? "Allowed: reminders go to “" + TaskCalendarExportService.AppCalendarName + "” in " + account + "."
                    : "Google needs your OK once before reminders go to “" + TaskCalendarExportService.AppCalendarName + "” in " + account + ".";
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(IsSetUp));
    }

    partial void OnIsGoogleChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        TaskCalendarDestination destination = value ? TaskCalendarDestination.GoogleCalendar : TaskCalendarDestination.Nowhere;
        _ = _reminders.UpdateAsync(current => current with { NewRemindersTo = destination });
    }

    [RelayCommand(CanExecute = nameof(CanAllow))]
    private async Task AllowAsync()
    {
        if (OpenBrowser is null)
        {
            return;
        }

        IsAllowing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string? problem = await _export.AllowExportAsync(OpenBrowser, timeout.Token);
            StatusText = problem ?? string.Empty;
        }
        finally
        {
            IsAllowing = false;
            Load();
        }
    }

    private bool CanAllow() => !IsAllowing;
}
