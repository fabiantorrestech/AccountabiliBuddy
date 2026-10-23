namespace AccountabiliBuddy.Core.Domain;

/// <summary>A paired Bluetooth audio device: its system id (stable while it stays paired) and its name.</summary>
public sealed record BluetoothAudioDevice(string Id, string Name);
