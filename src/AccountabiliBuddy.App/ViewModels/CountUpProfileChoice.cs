namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A choice for the count-up Quick Start profile in Settings; no id = a new "Quick Start" profile, made on first use.</summary>
public sealed record CountUpProfileChoice(Guid? ProfileId, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
