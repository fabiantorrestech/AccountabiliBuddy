namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The letters that label the open subtasks of a task: a, b, c … z, then aa, ab ….</summary>
public static class SubtaskLetters
{
    /// <summary>The label for the subtask at a 1-based place, e.g. 1 = "a.", 27 = "aa.".</summary>
    public static string Of(int number)
    {
        string letters = string.Empty;
        while (number > 0)
        {
            number--;
            letters = (char)('a' + number % 26) + letters;
            number /= 26;
        }

        return letters + ".";
    }
}
