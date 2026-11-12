namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// Whether the Today sidebar is slid open or hidden to its rail (brief §4.2, CN §3 PC). Plain logic, one instance per app run:
/// <list type="bullet">
/// <item>Outside a session with "Remember": it stays the way it was last left outside a session, until the app restarts (it starts
/// open). "Open" / "Rail" set it each time a session ends (and at start); Ctrl+\ still toggles it until then.</item>
/// <item>A session starting: hidden to the rail ("Hidden"), or open ("Shown"), or open but faded ("Quiet"). Ctrl+\ toggles it for
/// this session only: the outside memory is not touched.</item>
/// </list>
/// Only what is drawn; nothing here is a guard.
/// </summary>
public sealed class TodaySidebarState
{
    private bool _inSession;
    private bool _rememberedOpen = true;
    private TodaySidebarInSession _inSessionChoice;
    private TodaySidebarOutside _outsideChoice;

    public TodaySidebarState(TodaySidebarInSession inSessionChoice = TodaySidebarInSession.Hidden, TodaySidebarOutside outsideChoice = TodaySidebarOutside.Remember)
    {
        _inSessionChoice = inSessionChoice;
        _outsideChoice = outsideChoice;
        IsOpen = OutsideStart();
    }

    /// <summary>The sidebar is slid over the list (else only its rail shows).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Open during a session with "Quiet": drawn faded until the pointer or the keyboard is on it.</summary>
    public bool IsQuiet => IsOpen && _inSession && _inSessionChoice == TodaySidebarInSession.Quiet;

    public bool InSession => _inSession;

    /// <summary>Raised when <see cref="IsOpen"/> or <see cref="IsQuiet"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Ctrl+\ or the sidebar's own hide / show buttons.</summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (!_inSession && _outsideChoice == TodaySidebarOutside.Remember)
        {
            _rememberedOpen = IsOpen;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A session started or ended (called on every session change; nothing happens unless the state of the day changed).</summary>
    public void SessionChanged(bool inSession)
    {
        if (inSession == _inSession)
        {
            return;
        }

        _inSession = inSession;
        IsOpen = inSession ? _inSessionChoice != TodaySidebarInSession.Hidden : OutsideStart();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Settings › Today's side column changed: the choice for the state the day is in now applies at once.</summary>
    public void SettingsChanged(TodaySidebarInSession inSessionChoice, TodaySidebarOutside outsideChoice)
    {
        bool inSessionChanged = inSessionChoice != _inSessionChoice;
        bool outsideChanged = outsideChoice != _outsideChoice;
        _inSessionChoice = inSessionChoice;
        _outsideChoice = outsideChoice;
        if (_inSession && inSessionChanged)
        {
            IsOpen = inSessionChoice != TodaySidebarInSession.Hidden;
        }
        else if (!_inSession && outsideChanged)
        {
            // Switching to Remember keeps it as it is now (and remembers that).
            _rememberedOpen = outsideChoice == TodaySidebarOutside.Remember ? IsOpen : _rememberedOpen;
            IsOpen = OutsideStart();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool OutsideStart() => _outsideChoice switch
    {
        TodaySidebarOutside.Open => true,
        TodaySidebarOutside.Rail => false,
        _ => _rememberedOpen,
    };
}
