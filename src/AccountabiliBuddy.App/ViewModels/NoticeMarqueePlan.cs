namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// How the title bar's noise control shows a notice in its name box without widening it (owner 2026-11-14): the notice fades in at
/// the box's left edge, waits a moment, scrolls left at <see cref="Speed"/> until its end is in view, lingers <see cref="Linger"/>,
/// then fades out and the sound's name comes back. A notice that fits doesn't scroll. With reduced motion (the app's Animations Off)
/// it never scrolls: it fades in (trimmed to the box), holds, and fades out. Times are seconds from the start.
/// </summary>
public sealed class NoticeMarqueePlan
{
    /// <summary>Scroll speed in DIP per second (owner: about 40–60).</summary>
    public const double Speed = 50;

    /// <summary>The fade in and the fade out.</summary>
    public const double Fade = 0.25;

    /// <summary>Still at the start before scrolling, so the first words can be read.</summary>
    public const double StartPause = 0.6;

    /// <summary>Still at the end before fading out.</summary>
    public const double Linger = 2.0;

    /// <summary>Reduced motion: how long the trimmed notice holds between its fades.</summary>
    public const double ReducedHold = 3.0;

    public NoticeMarqueePlan(double boxWidth, double textWidth, bool reducedMotion)
    {
        BoxWidth = Math.Max(0, boxWidth);
        TextWidth = Math.Max(0, textWidth);
        ReducedMotion = reducedMotion;
        Travel = reducedMotion ? 0 : Math.Max(0, TextWidth - BoxWidth);
        Duration = reducedMotion
            ? Fade + ReducedHold + Fade
            : Fade + StartPause + Travel / Speed + Linger + Fade;
    }

    public double BoxWidth { get; }

    public double TextWidth { get; }

    public bool ReducedMotion { get; }

    /// <summary>How far the notice scrolls left in all (0 when it fits, or with reduced motion).</summary>
    public double Travel { get; }

    /// <summary>The whole showing, fades included.</summary>
    public double Duration { get; }

    /// <summary>The notice's horizontal offset at <paramref name="seconds"/> (0 or less: it only moves left).</summary>
    public double Offset(double seconds)
    {
        double scrolled = (seconds - Fade - StartPause) * Speed;
        return -Math.Clamp(scrolled, 0, Travel);
    }

    /// <summary>The notice's opacity at <paramref name="seconds"/>; the sound's name shows at one minus this.</summary>
    public double Opacity(double seconds)
    {
        if (seconds <= 0 || seconds >= Duration)
        {
            return 0;
        }

        if (seconds < Fade)
        {
            return seconds / Fade;
        }

        double left = Duration - seconds;
        return left < Fade ? left / Fade : 1;
    }
}
