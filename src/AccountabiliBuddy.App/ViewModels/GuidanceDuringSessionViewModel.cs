using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Switching to ProductaBuddy during a focus session (ADR 68): after this session ends (the usual wait, then no session running), or
/// now (what ending the session early costs, then the wait, with the session still running). Proceed or Cancel.
/// </summary>
public sealed partial class GuidanceDuringSessionViewModel : ObservableObject
{
    /// <param name="hasSetEnd">False for a session with no set end: "after it ends" means when it is ended by hand.</param>
    /// <param name="nowCost">"a 60 s wait and a reason", or "nothing extra" for a session that ends on command.</param>
    /// <param name="companionWait">The looser-change wait, e.g. "10 minutes".</param>
    public GuidanceDuringSessionViewModel(bool hasSetEnd, string nowCost, string companionWait)
    {
        NoSetEnd = !hasSetEnd;
        AfterText = $"After this session ends ({companionWait} from now at the earliest)";
        NowText = $"Now: costs what ending this session early costs ({nowCost}), then {companionWait}";
    }

    public static string Heading => $"Switch to {AppBrand.CompanionName} during this session?";

    public string AfterText { get; }

    public string NowText { get; }

    /// <summary>The session has no set end: the yellow note under "after this session ends" shows.</summary>
    public bool NoSetEnd { get; }

    public string NoSetEndNote => $"This session has no set end: \"after this session ends\" means {AppBrand.CompanionName} takes effect when you end it yourself.";

    [ObservableProperty]
    public partial bool AfterSessionChosen { get; set; } = true;

    [ObservableProperty]
    public partial bool NowChosen { get; set; }

    /// <summary>Raised with true to proceed or false to cancel; the dialog closes in response.</summary>
    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void Proceed() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
