namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The card at the top of the Shelf that offers the weekly tidy-up (spec 05 section 9): "Weekly tidy-up · 5 untouched for 3+ weeks".</summary>
/// <param name="Count">How many tasks are untouched.</param>
/// <param name="Weeks">The untouched setting in weeks.</param>
/// <param name="Day">The tidy-up day.</param>
public sealed record TidyUpCard(int Count, int Weeks, DayOfWeek Day)
{
    public string Title => "Weekly tidy-up · " + Count + " untouched for " + Weeks + "+ weeks";

    public string Sub => "About 2 minutes · " + Day + "s";
}
