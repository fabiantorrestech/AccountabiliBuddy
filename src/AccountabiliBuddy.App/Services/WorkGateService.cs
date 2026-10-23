using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The work-hours gate (ADR 28): while <see cref="WorkHoursGuard"/> guards and no session runs, the first listed app the
/// user reaches for once work hours start, and again after <see cref="RearmAfter"/> with no input, opens
/// <see cref="Current"/> instead of the plain notice. Calls, the browser and any other input never open it. It asks what
/// the user was about to do, keeps the answer as a come-back, and lets them continue after 60 seconds. A gate closed some
/// other way opens again on the next listed app; one open when work hours end closes, keeping what was typed. Use from
/// the UI thread only.
/// </summary>
public sealed class WorkGateService : IDisposable
{
    public static readonly TimeSpan RearmAfter = TimeSpan.FromMinutes(3);

    private readonly WorkHoursGuard _guard;
    private readonly IUserIdleDetector _idle;
    private readonly ComebackStore _comebacks;
    private readonly SessionController _sessions;
    private bool _armed;
    private bool _wasGuarding;

    public WorkGateService(WorkHoursGuard guard, IUserIdleDetector idle, ComebackStore comebacks, SessionController sessions)
    {
        _guard = guard;
        _idle = idle;
        _comebacks = comebacks;
        _sessions = sessions;
        _guard.OpenGate = TryOpen;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>The gate showing, or null.</summary>
    public WorkGateViewModel? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> opens or closes (the app shows or closes the window).</summary>
    public event EventHandler? Changed;

    /// <summary>The window was closed some other way: the next listed app opens it again.</summary>
    public void WindowClosed()
    {
        if (Current is not null)
        {
            _armed = true;
            SaveText(Current);
            Current = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Arms the gate (work hours starting, or a pause), counts its wait down, closes it when work ends. Runs every second.</summary>
    public void CheckNow()
    {
        bool guarding = _guard.IsGuarding;
        if (guarding && !_wasGuarding)
        {
            _armed = true; // Work hours just started.
        }

        _wasGuarding = guarding;
        if (!guarding)
        {
            _armed = false;
            Close();
            return;
        }

        if (Current is not null)
        {
            Current.Refresh(_sessions.Now);
        }
        else if (_idle.GetIdleTime() >= RearmAfter)
        {
            _armed = true;
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        if (_guard.OpenGate == TryOpen)
        {
            _guard.OpenGate = null;
        }
    }

    /// <summary>A listed app was just minimized: opens the gate when it's owed. True when it's showing (no notice then).</summary>
    private bool TryOpen()
    {
        CheckNow();
        if (Current is not null)
        {
            return true;
        }

        if (!_armed || _sessions.State.IsActive)
        {
            return false; // A session has its own enforcement: the dev tool is only minimized.
        }

        _armed = false;
        Current = new WorkGateViewModel(_guard.UntilText, _sessions.Now, Continue);
        Current.Refresh(_sessions.Now);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Continue(WorkGateViewModel gate)
    {
        if (gate == Current && gate.CanContinue)
        {
            Close();
        }
    }

    private void Close()
    {
        if (Current is not { } gate)
        {
            return;
        }

        SaveText(gate);
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveText(WorkGateViewModel gate)
    {
        if (gate.Text.Trim().Length > 0)
        {
            _ = _comebacks.AddAsync(gate.Text);
            gate.Text = string.Empty;
        }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }
}
