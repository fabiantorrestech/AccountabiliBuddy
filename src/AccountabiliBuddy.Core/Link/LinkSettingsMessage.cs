using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>The PC's settings for the device (see <see cref="ILinkSessionHost.GetSettingsAsync"/>) and their version (a hash).</summary>
public sealed record LinkSettingsMessage(string Version, JsonElement Content);
