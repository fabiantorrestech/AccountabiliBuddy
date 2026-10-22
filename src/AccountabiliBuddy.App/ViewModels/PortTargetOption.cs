namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Where ticked rules can go: another profile, or a new one (<paramref name="Profile"/> null).</summary>
public sealed record PortTargetOption(ProfileEditorViewModel? Profile)
{
    public string Label => Profile?.Name ?? "New profile…";

    /// <summary>What a screen reader says for the chosen one.</summary>
    public override string ToString()
    {
        return Label;
    }
}
