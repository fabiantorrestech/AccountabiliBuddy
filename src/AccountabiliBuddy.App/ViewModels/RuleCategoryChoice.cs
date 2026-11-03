namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A day plan category a calendar rule or keyword can give (every category but Sleep and Pre-Work, ADR 58).</summary>
public sealed record RuleCategoryChoice(Guid Id, string Name);
