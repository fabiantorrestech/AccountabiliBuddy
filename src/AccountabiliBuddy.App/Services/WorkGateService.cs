using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The work-hours gate (ADR 27): while <see cref="WorkHoursGuard"/> guards, the first input after <see cref="RearmAfter"/>
/// with none (and the first input once work hours start) opens <see cref="Current"/>. It asks what the user was about to do,
/// keeps the answer as a come-back, and lets them continue after 60 seconds. A gate closed some other way opens again at
/// the next check; one open when work hours end closes, keeping what was typed. Use from the UI thread only.
/// </summary>
public sealed class WorkGateService : IDisposable
{
    public static readonly TimeSpan RearmAfter = TimeSpan.FromMinutes(3);

    // Input this recent counts as "the user is here".
    private static readonly TimeSpan FreshInput = TimeSpan.FromSeconds(2);

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
        _sessions.Ticked += OnTicked;
    }

    /// <summary>The gate showing, or null.</summary>
    public WorkGateViewModel? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> opens or closes (the app shows or closes the window).</summary>
    public event EventHandler? Changed;

    /// <summary>The window was closed some other way: it opens again at the next check while still owed.</summary>
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

    /// <summary>Arms and opens the gate. Runs every second.</summary>
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

        TimeSpan idleFor = _idle.GetIdleTime();
        if (Current is not null)
        {
            Current.Refresh(_sessions.Now);
            return;
        }

        if (idleFor >= RearmAfter)
        {
            _armed = true;
        }
        else if (_armed && idleFor < FreshInput)
        {
            _armed = false;
            Current = new WorkGateViewModel(_guard.UntilText, _sessions.Now, Continue);
            Current.Refresh(_sessions.Now);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
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
