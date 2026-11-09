using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The tie-break parts ADR 72 added to the shared items: the labels and the keeper, each only when set, so an item without
/// them breaks ties exactly as before, and a copy that kept them wins an equal-stamp tie against one passed on without them.
/// </summary>
public static class SharedItemTie
{
    public static IEnumerable<string> Extra(IReadOnlyList<string>? labels, Guid? localTo)
    {
        if (labels is { Count: > 0 })
        {
            yield return TaskLabels.TieText(labels);
        }

        if (localTo is Guid keeper)
        {
            yield return keeper.ToString("D");
        }
    }
}
