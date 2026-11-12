using System.Globalization;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Where should your Tasks view come from?" (owner, 2026-11-11), in the setup assistant and Settings › Tasks › New tasks: a backup file
/// from this PC (<see cref="TaskAccountSettingsService.FromBackup"/>, its password asked when sealed), the copy stored in the account
/// (<see cref="TaskAccountSettingsService.AccountCopy"/>, unavailable when it holds nothing) or a fresh start
/// (<see cref="TaskAccountSettingsService.Fresh"/>), each with its time in 12-hour local time. <see cref="ApplyAsync"/> makes the picked
/// copy the account's (the default view and the picked calendar, for every device). Use from the UI thread only.
/// </summary>
public sealed partial class TaskViewChooserViewModel : ObservableObject
{
    public const string Explanation =
        "Sets the view the Shelf opens with and the calendar for tasks with a due date, for your whole account: every device follows.";

    private readonly TaskAccountSettingsService _account;
    private readonly TaskViewService? _views;
    private readonly Func<string, string?, BackupContent> _readBackup;
    private readonly TimeProvider _time;
    private TaskAccountSettingsChoice? _backupChoice;
    private string? _backupText;
    private string? _backupFileName;

    /// <param name="readBackup">Opens a backup file's text with its password (null for none): <see cref="BackupService.Read"/>.</param>
    public TaskViewChooserViewModel(TaskAccountSettingsService account, TaskViewService? views, Func<string, string?, BackupContent> readBackup, TimeProvider time)
    {
        _account = account;
        _views = views;
        _readBackup = readBackup;
        _time = time;
        AccountText = AccountWhat = BackupWhat = FreshWhat = Password = StatusText = string.Empty;
        BackupText = "No file chosen yet.";
        Refresh();
        Picked = HasAccountCopy ? TaskAccountSettingsOrigin.Account : TaskAccountSettingsOrigin.Fresh;
    }

    /// <summary>Set by the view: shows an open-file dialog for a backup (.abbackup) and returns the chosen path.</summary>
    public Func<Task<string?>>? PickBackupFile { get; set; }

    public string ExplanationText => Explanation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackupPicked), nameof(IsAccountPicked), nameof(IsFreshPicked))]
    public partial TaskAccountSettingsOrigin Picked { get; set; }

    public bool IsBackupPicked
    {
        get => Picked == TaskAccountSettingsOrigin.Backup;
        set => Pick(TaskAccountSettingsOrigin.Backup, value);
    }

    public bool IsAccountPicked
    {
        get => Picked == TaskAccountSettingsOrigin.Account;
        set => Pick(TaskAccountSettingsOrigin.Account, value);
    }

    public bool IsFreshPicked
    {
        get => Picked == TaskAccountSettingsOrigin.Fresh;
        set => Pick(TaskAccountSettingsOrigin.Fresh, value);
    }

    // ---- Use a backup file from this PC ----

    /// <summary>"Backup made Nov 3, 2026, 9:12 PM", or why there is none yet.</summary>
    [ObservableProperty]
    public partial string BackupText { get; set; }

    /// <summary>What the backup's copy sets ("The Shelf opens with “Work” · no picked calendar"); empty before a file is open.</summary>
    [ObservableProperty]
    public partial string BackupWhat { get; set; }

    [ObservableProperty]
    public partial bool NeedsPassword { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    // ---- Use the copy stored in your account ----

    /// <summary>The account holds something to use (a default view or a picked calendar, chosen or taken over).</summary>
    [ObservableProperty]
    public partial bool HasAccountCopy { get; set; }

    /// <summary>"Chosen Nov 11, 2026, 4:05 PM", "Kept from a device's earlier setting" or "Nothing stored in your account yet".</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; }

    [ObservableProperty]
    public partial string AccountWhat { get; set; }

    // ---- Start fresh ----

    [ObservableProperty]
    public partial string FreshWhat { get; set; }

    /// <summary>What the last action did or why it couldn't (a wrong password, no file yet, applied).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => StatusText.Length > 0;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Reads the account's copy again (it may have arrived from another device meanwhile).</summary>
    public void Refresh()
    {
        // Only the two settings the chooser sets count (not "Task reminders ring on", which it leaves alone).
        SyncTaskAccountSettings current = _account.Current;
        TaskAccountSettingsChoice copy = _account.AccountCopy();
        HasAccountCopy = current.DefaultViewStamp is not null || current.PickedCalendarStamp is not null;
        DateTimeOffset? chosen = new[] { current.DefaultViewStamp, current.PickedCalendarStamp }
            .Where(TaskAccountSettingsEdits.IsChosen)
            .Select(stamp => (DateTimeOffset?)stamp!.At)
            .Max();
        AccountText = !HasAccountCopy
            ? "Nothing stored in your account yet."
            : chosen is DateTimeOffset at
                ? "Chosen " + When(at) + "."
                : "Kept from a device's earlier setting.";
        AccountWhat = HasAccountCopy ? What(copy) : string.Empty;
        FreshWhat = What(TaskAccountSettingsService.Fresh);
        if (!HasAccountCopy && Picked == TaskAccountSettingsOrigin.Account)
        {
            Picked = TaskAccountSettingsOrigin.Fresh;
        }
    }

    /// <summary>Picks a backup file and opens it (asking for its password when it has one).</summary>
    [RelayCommand]
    private async Task ChooseBackupFileAsync()
    {
        if (PickBackupFile is null || await PickBackupFile() is not string path)
        {
            return;
        }

        StatusText = string.Empty;
        _backupChoice = null;
        BackupWhat = string.Empty;
        NeedsPassword = false;
        Password = string.Empty;
        _backupFileName = Path.GetFileName(path);
        try
        {
            _backupText = await File.ReadAllTextAsync(path);
            if (BackupService.Inspect(_backupText) == BackupProtection.Password)
            {
                NeedsPassword = true;
                BackupText = $"{_backupFileName} is password-protected. Enter its password.";
                Picked = TaskAccountSettingsOrigin.Backup;
                return;
            }

            await OpenBackupAsync(null);
        }
        catch (FormatException ex)
        {
            _backupText = null;
            BackupText = $"{_backupFileName}: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _backupText = null;
            BackupText = $"Couldn't read {_backupFileName}: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task OpenWithPasswordAsync()
    {
        return OpenBackupAsync(Password);
    }

    /// <summary>Makes the picked copy the account's settings (a choice made now, for every device).</summary>
    /// <returns>False when nothing could be applied (Use a backup file picked without an opened backup).</returns>
    public async Task<bool> ApplyAsync()
    {
        TaskAccountSettingsChoice? choice = Picked switch
        {
            TaskAccountSettingsOrigin.Backup => _backupChoice,
            TaskAccountSettingsOrigin.Account => HasAccountCopy ? _account.AccountCopy() : null,
            _ => TaskAccountSettingsService.Fresh,
        };
        if (choice is null)
        {
            StatusText = Picked == TaskAccountSettingsOrigin.Backup
                ? "Choose a backup file first, or pick another option."
                : "Your account has nothing stored yet. Pick another option.";
            return false;
        }

        // Start fresh over an account that holds neither setting changes nothing: a missing choice already means the built-in Default
        // view and no picked calendar. Writing it would stamp it now, and once this PC links (or the account's copy arrives) that newer
        // stamp would reset the group's default view and turn its picked calendar off.
        LastApplyWrote = !(choice.Origin == TaskAccountSettingsOrigin.Fresh && !HasAccountCopy);
        if (LastApplyWrote)
        {
            await _account.ApplyAsync(choice);
        }

        StatusText = "Done. " + What(choice);
        // The backup file's text and password aren't kept once its copy is applied.
        _backupText = null;
        Password = string.Empty;
        Refresh();
        return true;
    }

    /// <summary>The last <see cref="ApplyAsync"/> wrote to the account (false: Start fresh over an account that holds nothing).</summary>
    public bool LastApplyWrote { get; private set; }

    /// <summary>Forgets the opened backup (its text, password and copy) and goes back to the default pick (Settings' Cancel).</summary>
    public void Forget()
    {
        _backupText = null;
        _backupChoice = null;
        _backupFileName = null;
        Password = string.Empty;
        NeedsPassword = false;
        BackupText = "No file chosen yet.";
        BackupWhat = StatusText = string.Empty;
        Refresh();
        Picked = HasAccountCopy ? TaskAccountSettingsOrigin.Account : TaskAccountSettingsOrigin.Fresh;
    }

    private async Task OpenBackupAsync(string? password)
    {
        if (_backupText is not string text)
        {
            return;
        }

        IsBusy = true;
        try
        {
            BackupContent content = await Task.Run(() => _readBackup(text, password));
            NeedsPassword = false;
            Password = string.Empty;
            _backupChoice = TaskAccountSettingsService.FromBackup(content);
            Picked = TaskAccountSettingsOrigin.Backup;
            if (_backupChoice is null)
            {
                BackupText = $"Backup made {When(content.CreatedAt)}, but it holds no Tasks view settings. Pick another backup or option.";
                BackupWhat = string.Empty;
                return;
            }

            BackupText = $"Backup made {When(content.CreatedAt)}"
                + (_backupChoice.EditedAt is DateTimeOffset chosen ? $" · chosen {When(chosen)}." : ".");
            BackupWhat = What(_backupChoice);
        }
        catch (BackupPasswordException ex)
        {
            NeedsPassword = true;
            BackupText = ex.Message;
        }
        catch (FormatException ex)
        {
            BackupText = $"{_backupFileName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // "The Shelf opens with “Work” · tasks with a due date go to a calendar you picked".
    private string What(TaskAccountSettingsChoice choice)
    {
        string view;
        if (choice.DefaultViewId is not Guid id || id == TaskView.DefaultId)
        {
            view = "The Shelf opens with Default";
        }
        else if (_views?.Views.FirstOrDefault(candidate => candidate.Id == id) is TaskView known)
        {
            view = $"The Shelf opens with “{known.Name}”";
        }
        else
        {
            view = "The Shelf opens with a view that isn't on this PC (Default until it arrives)";
        }

        string calendar = choice.PickedCalendarId is null
            ? "tasks with a due date go where Add new reminders to says"
            : "tasks with a due date go to a calendar you picked";
        return $"{view} · {calendar}.";
    }

    // "Nov 11, 2026, 4:05 PM" in this PC's time zone (12-hour, whatever the system's setting).
    private string When(DateTimeOffset at)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).DateTime;
        return local.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture);
    }

    private void Pick(TaskAccountSettingsOrigin origin, bool selected)
    {
        if (selected && (origin != TaskAccountSettingsOrigin.Account || HasAccountCopy))
        {
            Picked = origin;
        }
    }
}
