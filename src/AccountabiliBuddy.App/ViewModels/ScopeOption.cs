namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One scope a stacked-prompt row can be answered for, e.g. "The whole site (reddit.com)".</summary>
public sealed record ScopeOption(string Label, ClassifyScope Scope)
{
    public override string ToString() => Label;
}
