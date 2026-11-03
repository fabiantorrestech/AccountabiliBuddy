using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quit safeguard's card (ADR 59), on Settings › Guards and the migration assistant's page. Each tick is a change right away: a stricter
/// one applies, a looser one waits and the box shows the choice with when it applies. Leaving "All the time" first asks for the spare key.
/// Use from the UI thread only.
/// </summary>
public sealed partial class QuitGuardViewModel : ObservableObject
{
    private readonly QuitGuardService _service;
    private bool _loading;

    // The change waiting on the spare key being typed.
    private QuitGuardSettings? _heldForKey;

    public QuitGuardViewModel(QuitGuardService service, SessionController sessions)
    {
        _service = service;
        InFocus = service.InFocusSession;
        sessions.StateChanged += (_, _) => InFocus = service.InFocusSession;
        service.Changed += (_, _) => Load();
        Load();
    }

    /// <summary>The day plan changed: a category may have come or gone.</summary>
    public void Refresh()
    {
        Load();
    }

    /// <summary>Opens the window that shows a new spare key to write down and retype. Set by the app.</summary>
    public Action? ShowSpareKey { get; set; }

    public ObservableCollection<QuitCategoryRowViewModel> Categories { get; } = [];

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool AllTheTime { get; set; }

    [ObservableProperty]
    public partial bool DuringSession { get; set; }

    /// <summary>A focus session the user started is running: nothing changes now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool InFocus { get; set; }

    public bool CanEdit => !InFocus;

    /// <summary>"Takes effect at 4:05 PM…"; empty when no change waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial string PendingText { get; set; } = string.Empty;

    public bool HasPending => PendingText.Length > 0;

    /// <summary>The box for the spare key shows.</summary>
    [ObservableProperty]
    public partial bool AsksKey { get; set; }

    [ObservableProperty]
    public partial string TypedKey { get; set; } = string.Empty;

    /// <summary>What just happened that needs saying; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;

    public bool HasMessage => Message.Length > 0;

    [RelayCommand]
    private async Task ConfirmKeyAsync()
    {
        if (_heldForKey is not QuitGuardSettings held)
        {
            return;
        }

        QuitGuardChange result = await _service.ChangeAsync(held, TypedKey);
        if (result == QuitGuardChange.WrongKey)
        {
            Message = "That isn't your spare key.";
            return;
        }

        ClearKeyPrompt();
        Say(result);
        Load();
    }

    [RelayCommand]
    private void CancelKey()
    {
        ClearKeyPrompt();
        Message = string.Empty;
        Load();
    }

    [RelayCommand]
    private async Task CancelPendingAsync()
    {
        Message = string.Empty;
        await _service.CancelPendingAsync();
    }

    partial void OnIsOnChanged(bool value) => Apply();

    partial void OnAllTheTimeChanged(bool value) => Apply();

    partial void OnDuringSessionChanged(bool value) => Apply();

    private void Apply()
    {
        if (!_loading)
        {
            _ = ApplyAsync();
        }
    }

    private async Task ApplyAsync()
    {
        QuitGuardSettings wanted = new(IsOn, AllTheTime, DuringSession, Categories.Where(row => row.IsTicked).Select(row => row.Id).ToList());
        QuitGuardChange result = await _service.ChangeAsync(wanted);
        if (result == QuitGuardChange.NeedsSpareKey)
        {
            _heldForKey = wanted;
            AsksKey = true;
            TypedKey = string.Empty;
            Message = string.Empty;
            Load();
            return;
        }

        Say(result);
        if (result == QuitGuardChange.NeedsSpareKeySetup)
        {
            ShowSpareKey?.Invoke();
        }

        // The boxes go back to what the service holds when the change was refused.
        Load();
    }

    private void Say(QuitGuardChange result)
    {
        Message = result switch
        {
            QuitGuardChange.RefusedInFocus => "Locked while a focus session you started is running.",
            QuitGuardChange.NeedsSpareKeySetup => "\"All the time\" needs a spare key, so there is a way back out. Write it down in the window that opened, then tick this again.",
            _ => string.Empty,
        };
    }

    private void ClearKeyPrompt()
    {
        _heldForKey = null;
        AsksKey = false;
        TypedKey = string.Empty;
    }

    private void Load()
    {
        _loading = true;
        QuitGuardSettings settings = _service.Editing;
        DayPlan plan = _service.Plan;
        IReadOnlyCollection<Guid> ticked = QuitGuard.CategoriesOf(settings, plan);
        IsOn = settings.On;
        AllTheTime = settings.AllTheTime;
        DuringSession = settings.DuringSession;
        // The same categories keep their rows: this also runs from a click on one of them, which must not take its box away.
        if (Categories.Select(row => row.Id).SequenceEqual(plan.Categories.Select(category => category.Id)))
        {
            foreach (QuitCategoryRowViewModel row in Categories)
            {
                row.Show(ticked.Contains(row.Id));
            }
        }
        else
        {
            Categories.Clear();
            foreach (DayCategory category in plan.Categories)
            {
                Categories.Add(new QuitCategoryRowViewModel(category.Id, category.Name, BrushOf(category.Color), ticked.Contains(category.Id), Apply));
            }
        }

        PendingText = _service.Pending is PendingQuitGuard pending
            ? $"This takes effect at {TimeZoneInfo.ConvertTime(pending.ReadyAt, TimeZoneInfo.Local):t}, or after a session running then. Until then, Quit stays locked as before."
            : string.Empty;
        _loading = false;
    }

    private static IBrush BrushOf(string color)
    {
        return Color.TryParse(color, out Color parsed) ? new SolidColorBrush(parsed) : Brushes.Transparent;
    }
}
