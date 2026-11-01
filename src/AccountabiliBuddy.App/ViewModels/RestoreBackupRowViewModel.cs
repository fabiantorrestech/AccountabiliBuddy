namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One backup on Google Drive in the setup assistant's "Restore from an older device" step (ADR 55): e.g. "Pixel 7", "Backed up Oct 28, 2026", and its Restore button.</summary>
/// <param name="DeviceName">The removed device's name (the folder it is in).</param>
/// <param name="DateText">When it was backed up: from the file's name, else Drive's modified time.</param>
/// <param name="RestoreCommand">Downloads it, opens it with this PC's group key and brings the device's history in.</param>
public sealed record RestoreBackupRowViewModel(string DeviceName, string DateText, System.Windows.Input.ICommand RestoreCommand);
