namespace AccountabiliBuddy.App.ViewModels;

/// <summary>When an unlock ends, as offered on the Session tab.</summary>
public sealed record RelockChoice(RelockChoice.Kind When, string Label)
{
    public enum Kind
    {
        Midnight,
        AtTime,
        Never,
    }

    public static IReadOnlyList<RelockChoice> All { get; } = new[]
    {
        new RelockChoice(Kind.Midnight, "Relock at midnight"),
        new RelockChoice(Kind.AtTime, "Relock at a time…"),
        new RelockChoice(Kind.Never, "Don't relock (I'll lock again myself)"),
    };

    public override string ToString() => Label;
}
