using AccountabiliBuddy.Core.Domain;
using Material.Icons;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// ADR 93 (ADR 32): the PC's Material icon for each label-group icon key (<see cref="TaskLabelGroupIcons"/>). The phone maps the same keys to
/// material-icons-extended; the same key gets the same meaning on both. An unknown key (a newer app's) shows the default's icon.
/// </summary>
public static class LabelGroupIconKinds
{
    private static readonly IReadOnlyDictionary<string, MaterialIconKind> Kinds = new Dictionary<string, MaterialIconKind>(StringComparer.Ordinal)
    {
        [TaskLabelGroupIcons.Default] = MaterialIconKind.FolderOutline,
        ["work"] = MaterialIconKind.BriefcaseOutline,
        ["home"] = MaterialIconKind.HomeOutline,
        ["person"] = MaterialIconKind.AccountOutline,
        ["people"] = MaterialIconKind.AccountGroupOutline,
        ["school"] = MaterialIconKind.SchoolOutline,
        ["heart"] = MaterialIconKind.HeartOutline,
        ["code"] = MaterialIconKind.CodeTags,
        ["money"] = MaterialIconKind.CashMultiple,
        ["cart"] = MaterialIconKind.CartOutline,
        ["fitness"] = MaterialIconKind.Dumbbell,
        ["book"] = MaterialIconKind.BookOpenVariant,
        ["star"] = MaterialIconKind.StarOutline,
        ["flag"] = MaterialIconKind.FlagOutline,
        ["lightbulb"] = MaterialIconKind.LightbulbOutline,
        ["travel"] = MaterialIconKind.Airplane,
        ["music"] = MaterialIconKind.Music,
        ["palette"] = MaterialIconKind.PaletteOutline,
        ["calendar"] = MaterialIconKind.CalendarOutline,
        ["chat"] = MaterialIconKind.ChatOutline,
        ["health"] = MaterialIconKind.HeartPulse,
        ["pets"] = MaterialIconKind.Paw,
        ["food"] = MaterialIconKind.SilverwareForkKnife,
        ["build"] = MaterialIconKind.Hammer,
    };

    /// <summary>The icon for <paramref name="key"/>; the default's for an unknown or missing key.</summary>
    public static MaterialIconKind Of(string? key)
    {
        return key is not null && Kinds.TryGetValue(key, out MaterialIconKind kind) ? kind : Kinds[TaskLabelGroupIcons.Default];
    }
}
