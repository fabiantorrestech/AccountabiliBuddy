using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>Carries out one <see cref="ArmedEndKind"/> for <see cref="ArmedEndService"/> (ADR 66).</summary>
public interface IArmedEndHandler
{
    ArmedEndKind Kind { get; }

    /// <summary>The countdown's label, e.g. "Session ends"; shown with the time left on the tile, Today and the tray.</summary>
    string Label(ArmedEnd end);

    /// <summary>The notice's title once it applied, e.g. "Session ended".</summary>
    string DoneTitle(ArmedEnd end);

    /// <summary>False once what it ends is gone (the session ended some other way, the work block is over): it is dropped.</summary>
    bool IsStillDue(ArmedEnd end);

    /// <summary>Applies it now, with the reason that was typed (empty for a kind that takes none); returns the notice to show.</summary>
    Task<string> ApplyAsync(ArmedEnd end, string reason);
}
