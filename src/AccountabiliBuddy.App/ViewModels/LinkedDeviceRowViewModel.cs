namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A paired phone, tablet or watch in the Setup tab: its name, whether it's connected or when it was last seen, and an
/// icon for its kind.
/// </summary>
public sealed record LinkedDeviceRowViewModel(Guid Id, string Name, string Detail, string Icon = "📱");
