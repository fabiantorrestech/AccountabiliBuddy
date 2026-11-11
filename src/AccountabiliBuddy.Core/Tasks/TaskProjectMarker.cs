namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// A project marker at the start of a title ("Name - ", "[Name]", "Name 123 - ") found by <see cref="TaskProjectMarkers"/>: the text
/// it takes, and the label or tag it suggests.
/// </summary>
/// <param name="Full">The marker as typed, with the space after it (what is cut from the title if it is taken).</param>
/// <param name="Raw">The name inside it.</param>
/// <param name="LabelId">The label it names, when its first word is a label.</param>
/// <param name="Tag">The tag it suggests: its second word, or the first when that is no label.</param>
/// <param name="Rest">The title without the marker.</param>
public sealed record TaskProjectMarker(string Full, string Raw, Guid? LabelId, string? Tag, string Rest);
