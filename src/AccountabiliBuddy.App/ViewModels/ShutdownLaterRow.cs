namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A Later item due soon, on the routine's look-ahead step.</summary>
/// <param name="DueText">E.g. "tomorrow" or "overdue · Mon, Nov 2".</param>
public sealed record ShutdownLaterRow(string Title, string DueText);
