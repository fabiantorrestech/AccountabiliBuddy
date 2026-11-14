using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// ADR 89 (LB1b/LB1c): a view that filters ONLY by labels or tags this device doesn't show would open as an empty page. It opens as the
/// Default view instead, with the note "Uses a label from &lt;device&gt;. Showing Default." on the page. Display only: the saved view and the
/// remembered one are unchanged. The phone's twin is ViewFallback.kt (the same rule, the same parity fixtures).
/// </summary>
public static class TaskViewFallback
{
    /// <summary>
    /// Whether <paramref name="config"/> names labels or tags, none of them is shown here, and nothing else filters it (no "only" filter, no
    /// No label): such a page would be empty for a reason the user can't see. A view with any shown label or tag, or any other filter, is
    /// shown as it is (its hidden parts match nothing, as before).
    /// </summary>
    public static bool ShouldFallBack(TaskViewConfig config, IEnumerable<TaskLabel> labels, Func<TaskLabel, bool> labelShown, Func<string, bool> tagShown)
    {
        List<TaskLabel> live = labels.Where(label => label.IsLive).ToList();
        bool named = config.Labels is { Count: > 0 } || config.Tags is { Count: > 0 };
        bool onlyByLabels = config.Only == TaskViewFilter.None && !config.NoLabel;
        bool anyHidden = (config.Labels ?? []).Any(id => live.Any(label => label.Id == id && !labelShown(label)))
            || (config.Tags ?? []).Any(tag => !tagShown(tag));
        bool nothingShown = !(config.Labels ?? []).Any(id => live.Any(label => label.Id == id && labelShown(label)))
            && !(config.Tags ?? []).Any(tagShown);
        return named && onlyByLabels && anyHidden && nothingShown;
    }
}
