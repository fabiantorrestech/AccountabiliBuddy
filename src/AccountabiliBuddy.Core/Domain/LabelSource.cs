namespace AccountabiliBuddy.Core.Domain;

public enum LabelSource
{
    /// <summary>User answered the "classify this?" prompt for unknown activity.</summary>
    ClassifyPrompt,
    /// <summary>User disagreed with an existing classification.</summary>
    Override,
}
