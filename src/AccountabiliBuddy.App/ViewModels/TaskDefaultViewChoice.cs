namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A view Settings › Tasks › Default view can pick (the Shelf opens with it on every device of the account).</summary>
public sealed record TaskDefaultViewChoice(Guid Id, string Name)
{
    public override string ToString() => Name;
}
