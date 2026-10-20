namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A paired phone or tablet in the Setup tab: its name and whether it's connected or when it was last seen.</summary>
public sealed record LinkedDeviceRowViewModel(Guid Id, string Name, string Detail);
