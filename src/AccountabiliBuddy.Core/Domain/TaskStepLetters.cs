namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A step's letter from its place among all of its task's steps (ADR 76): 0 = "a", 25 = "z", 26 = "aa". A step is referred to
/// as its task's number plus its letter ("142b").
/// </summary>
public static class TaskStepLetters
{
    /// <summary>The letter for the step at a 0-based place.</summary>
    public static string Of(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        string letters = string.Empty;
        int number = index + 1;
        while (number > 0)
        {
            number--;
            letters = (char)('a' + number % 26) + letters;
            number /= 26;
        }

        return letters;
    }

    /// <summary>The 0-based place a letter stands for ("a" = 0, "aa" = 26); null when it isn't 1 to 4 lower-case letters.</summary>
    public static int? IndexOf(string letters)
    {
        if (string.IsNullOrEmpty(letters) || letters.Length > 4 || !letters.All(letter => letter is >= 'a' and <= 'z'))
        {
            return null;
        }

        int number = 0;
        foreach (char letter in letters)
        {
            number = number * 26 + (letter - 'a' + 1);
        }

        return number - 1;
    }
}
