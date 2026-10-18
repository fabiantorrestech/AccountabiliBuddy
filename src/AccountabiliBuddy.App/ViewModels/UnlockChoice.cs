using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>An unlock option as offered on the Session tab.</summary>
public sealed record UnlockChoice(UnlockOption Option, string Label)
{
    public override string ToString() => Label;
}
