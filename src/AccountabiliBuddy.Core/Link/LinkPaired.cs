namespace AccountabiliBuddy.Core.Link;

/// <summary>Pairing worked: the device keeps its id and token (sealed) and signs in with them from now on.</summary>
public sealed record LinkPaired(Guid DeviceId, string Token, string PcName);
