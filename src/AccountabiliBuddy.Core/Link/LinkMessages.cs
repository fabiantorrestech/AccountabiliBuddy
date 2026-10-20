namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// The message types of the link protocol (ADR 14) and the body each one carries. A connection starts with
/// <see cref="Pair"/> (a new device) or <see cref="Hello"/> (a paired one); anything else first is refused.
/// </summary>
public static class LinkMessages
{
    public const int ProtocolVersion = 1;

    // Device → PC.

    /// <summary><see cref="LinkPairRequest"/>: trade the one-time code for a device token.</summary>
    public const string Pair = "pair";

    /// <summary><see cref="LinkHello"/>: a paired device signs in with its token.</summary>
    public const string Hello = "hello";

    /// <summary><see cref="LinkStartRequest"/>: start a session.</summary>
    public const string Start = "start";

    /// <summary>No body: ask to end the running session early; the PC answers with <see cref="EndEarlyTerms"/>.</summary>
    public const string EndEarlyBegin = "endEarlyBegin";

    /// <summary><see cref="LinkEndEarlyRequest"/>: end it, after the wait, with the reason.</summary>
    public const string EndEarly = "endEarly";

    /// <summary>No body: the device forgets this PC; the PC removes it too.</summary>
    public const string Unpair = "unpair";

    // PC → device.

    /// <summary><see cref="LinkPaired"/>: the device's id and token.</summary>
    public const string Paired = "paired";

    /// <summary><see cref="LinkWelcome"/>: signed in.</summary>
    public const string Welcome = "welcome";

    /// <summary><see cref="LinkState"/>: the session and profiles, sent after sign-in and on every change.</summary>
    public const string State = "state";

    /// <summary>
    /// <see cref="LinkSettingsMessage"/>: the PC's profiles, sleep and day bar settings and upcoming events, sent after
    /// sign-in when the device's copy is older, and whenever they change (L2: no more backup imports).
    /// </summary>
    public const string Settings = "settings";

    /// <summary><see cref="LinkEndEarlyTerms"/>: the wait and the reason length for ending early.</summary>
    public const string EndEarlyTerms = "endEarlyTerms";

    /// <summary><see cref="LinkResult"/>: how a request went.</summary>
    public const string Result = "result";

    /// <summary><see cref="LinkResult"/>: the connection is refused (bad code or token, unpaired); the PC closes it.</summary>
    public const string Refused = "refused";
}
