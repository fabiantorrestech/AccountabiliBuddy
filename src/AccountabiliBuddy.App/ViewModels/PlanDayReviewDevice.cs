namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in the "Plan day(s)" review: its name, when its blocks come back on, blocks with their own time.</summary>
public sealed record PlanDayReviewDevice(string Name, bool IsPhone, string Time, IReadOnlyList<string> OwnTimes)
{
    public bool IsPc => !IsPhone;

    public bool HasOwnTimes => OwnTimes.Count > 0;
}
