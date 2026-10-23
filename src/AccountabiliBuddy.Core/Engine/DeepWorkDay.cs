namespace AccountabiliBuddy.Core.Engine;

/// <summary>One day's deep work: counted focus across the user's devices, and its longest unbroken stretch.</summary>
public sealed record DeepWorkDay(DateOnly Date, TimeSpan Focused, TimeSpan Longest);
