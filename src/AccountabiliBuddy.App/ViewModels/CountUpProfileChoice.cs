namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A choice for the "Quick Start with Default" profile in Settings; no id = a new "QS - Default" profile with its starting rules.</summary>
public sealed record CountUpProfileChoice(Guid? ProfileId, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
