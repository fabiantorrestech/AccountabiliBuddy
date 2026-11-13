namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a search hit matched (ADR 76).</summary>
public enum TaskSearchMatch
{
    /// <summary>The task's number or one of its steps' references ("142", "142b").</summary>
    Id,

    Title,

    Step,

    Note,

    Tag,

    /// <summary>The task's label ("@label").</summary>
    Label,

    /// <summary>The words are spread over the task's title, steps, note and tags.</summary>
    Text,
}
