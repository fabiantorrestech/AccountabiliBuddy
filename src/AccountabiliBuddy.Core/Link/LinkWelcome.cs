namespace AccountabiliBuddy.Core.Link;

/// <summary>Signed in; a <see cref="LinkState"/> follows.</summary>
public sealed record LinkWelcome(string PcName, int ProtocolVersion);
