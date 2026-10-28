namespace AccountabiliBuddy.Core.Engine;

/// <summary>A stretch of whole hours on the Stats page's Intensity view.</summary>
/// <param name="StartHour">0 to 23.</param>
/// <param name="EndHour">After <paramref name="StartHour"/>, up to 24.</param>
/// <param name="Share">The part of all focus that falls in it, 0 to 1.</param>
/// <param name="FocusMinutes">Focus minutes in it, added up over the range.</param>
/// <param name="DriftCount">Drift visits that began in it.</param>
public sealed record IntensityWindow(int StartHour, int EndHour, double Share, double FocusMinutes, int DriftCount);
