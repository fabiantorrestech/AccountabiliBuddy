namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A Surface2 card under a Stats chart: a title, a big value and a Muted line.</summary>
/// <param name="Opens">The view a click on the card opens; null when the card isn't a link.</param>
public sealed record StatsCard(string Title, string Value, string Detail, StatsViewKind? Opens = null)
{
    public bool IsLink => Opens is not null;

    /// <summary>A long value (a site, a date) is set smaller so it fits the card.</summary>
    public bool IsLong => Value.Length > 9;
}
