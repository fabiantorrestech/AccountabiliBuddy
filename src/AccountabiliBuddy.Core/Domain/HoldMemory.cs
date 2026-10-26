namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What the app remembers between checks (ADR 39): the blocks it password-locked, the blocks released for today
/// (spare key or Emergency release), and the blocks seen on outside its windows (what re-arm turns back on when a
/// day's rules list none).
/// </summary>
public sealed record HoldMemory(IReadOnlySet<string> PasswordHeld, IReadOnlySet<string> Released, IReadOnlySet<string> Known)
{
    public static HoldMemory Empty { get; } = new(new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
}
