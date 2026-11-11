namespace AccountabiliBuddy.Core.Tasks;

/// <summary>A running weekly tidy-up: its tasks in order and what was chosen for each (spec 05 section 9).</summary>
/// <param name="Weeks">The untouched setting it was made with (the flow's sub line).</param>
public sealed record TidyUpPlan(IReadOnlyList<TidyUpItem> Items, int Weeks)
{
    public int Total => Items.Count;

    /// <summary>How many tasks have a choice.</summary>
    public int Decided => Items.Count(item => item.Chosen is not null);

    /// <summary>"1 of 5 · untouched 3+ weeks", for the task at <paramref name="index"/>.</summary>
    public string Sub(int index)
    {
        return (index + 1) + " of " + Total + " · untouched " + Weeks + "+ weeks";
    }

    /// <summary>The plan with the choice for the task at <paramref name="index"/> set.</summary>
    public TidyUpPlan With(int index, TidyUpChoice choice)
    {
        List<TidyUpItem> items = Items.ToList();
        items[index] = items[index] with { Chosen = choice };
        return this with { Items = items };
    }
}
