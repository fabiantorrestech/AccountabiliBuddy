namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The answer to a day plan clash (<see cref="DayPlanClashRequest"/>).</summary>
public enum DayPlanClashChoice
{
    /// <summary>"Keep the one I was using": this PC's plan stays, and the other device takes it.</summary>
    KeepMine,

    /// <summary>"Use the latest" (also when the prompt is closed or ignored): the plan edited last wins.</summary>
    UseLatest,
}
