namespace AccountabiliBuddy.App.Services;

/// <summary>A value the Google setup guide offers to copy, e.g. a scope to paste into the Cloud Console.</summary>
public sealed record GoogleSetupCopyValue(string Label, string Value);
