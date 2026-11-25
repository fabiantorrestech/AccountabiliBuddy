namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Where the Shelf's resize handle sits between Next and To sort (Tasks redesign 4c), as <see cref="TaskColumnsPanel"/> last arranged them.
/// </summary>
/// <param name="CenterX">The centre of the gap between the two columns, from the panel's left edge.</param>
/// <param name="PairLeft">The left column's left edge.</param>
/// <param name="PairWidth">The two columns' widths together (the gap left out).</param>
/// <param name="LeadOnLeft">Next (the "lead") is the left one.</param>
public sealed record TaskColumnSplitPair(double CenterX, double PairLeft, double PairWidth, bool LeadOnLeft);
