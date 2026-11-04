namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// What a phone asks of a PC in a <see cref="SyncAction.KindClassifyEvent"/> action (ADR 64): how to classify one of the events in the PC's sync file. The
/// event is named by title and local times, as the phone already matches events across sources, since its keys stay on the PC.
/// The title is the user's own text: sealed in the file, never logged.
/// </summary>
/// <param name="Scope">One of the <c>Scope…</c> constants.</param>
/// <param name="Choice">One of the <c>Choice…</c> constants; a keyword rule is always <see cref="ChoiceCategory"/>.</param>
/// <param name="CategoryId">The day plan category, for <see cref="ChoiceCategory"/>.</param>
/// <param name="Keyword">The keyword, for <see cref="ScopeKeyword"/>.</param>
public sealed record SyncEventClassifyRequest(string Title, DateTime Start, DateTime End, string Scope, string Choice, Guid? CategoryId = null, string? Keyword = null)
{
    public const string ScopeThis = "this";
    public const string ScopeSeries = "series";
    public const string ScopeKeyword = "keyword";

    public const string ChoiceCategory = "category";
    public const string ChoiceDontClassify = "dontClassify";
    public const string ChoiceAutomatic = "automatic";
}
