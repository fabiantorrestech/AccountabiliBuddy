namespace AccountabiliBuddy.Core.Ports;

/// <param name="IsHostRegistered">True when the native messaging host is registered and points at an existing file.</param>
/// <param name="Problem">Why the registration is not usable, e.g. the host file was moved; null when fine.</param>
/// <param name="SharesRegistrationWith">Another browser that reads the same registration (granting or revoking
/// one affects both), e.g. Brave and Chrome on Windows; null when not shared.</param>
public sealed record BrowserSetupStatus(
    BrowserKind Browser,
    string DisplayName,
    bool IsInstalled,
    bool IsHostRegistered,
    string? Problem,
    string? SharesRegistrationWith);
