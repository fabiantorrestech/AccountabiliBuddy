namespace AccountabiliBuddy.Core.Ports;

/// <summary>Another blocker found installed on this computer. Status only: AccountabiliBuddy doesn't control it.</summary>
/// <param name="Name">The blocker's name as shown to the user, e.g. "Freedom".</param>
/// <param name="Version">Its version from the installer's record or its program file, if either gives one.</param>
public sealed record OtherBlocker(string Name, string? Version);
