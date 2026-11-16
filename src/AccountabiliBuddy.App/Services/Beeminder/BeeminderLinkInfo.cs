namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// What a goal row and the goal detail say about a goal's Beeminder read link (R8.2): the chip ("Beeminder · reading-slug · 3 safe days")
/// and, when the cached goals list has the road, the pace line ("Road · 3 pages a week · +2 within 1 day"). Both come from the
/// goals list already kept (ADR 43); nothing is fetched to build them.
/// </summary>
/// <param name="Chip">The chip's full text (the detail's chip, and every tooltip).</param>
/// <param name="RowChip">The Goals row's chip: the same without the slug ("Beeminder · 3 safe days"), so the safe days survive the list's narrowest width.</param>
/// <param name="Road">The pace line, or null when the cached answer has no road for this goal.</param>
/// <param name="IsDueToday">No buffer left (the chip is drawn amber).</param>
public sealed record BeeminderLinkInfo(string Chip, string RowChip, string? Road, bool IsDueToday);
