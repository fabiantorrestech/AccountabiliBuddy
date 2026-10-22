namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// The setup assistant's computer types (ADR 22): each only a starting set of switches, all changeable later. Safest
/// first: Local only makes no connections of its own; Work keeps less and never minimizes work windows.
/// </summary>
public enum ComputerType
{
    Personal,
    Work,
    LocalOnly,
}
