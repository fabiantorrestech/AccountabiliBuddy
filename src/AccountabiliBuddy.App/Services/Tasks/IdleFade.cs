using Avalonia;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// "Fade controls you're not using" (Tasks redesign 3.42-3.47) as app resources: the <c>idleFade</c> styles (Themes/AbStyles.axaml) read the
/// resting opacity of icons (<see cref="IconKey"/>, 22%) and of label row chips (<see cref="ChipKey"/>, 30%) from here, so turning the
/// setting off puts every faded control at full strength at once, on every page and pop-up. App-wide by nature, so static. UI thread only.
/// </summary>
public static class IdleFade
{
    /// <summary>The resting opacity of an <c>idleFade</c> icon control.</summary>
    public const string IconKey = "Ab.IdleFadeIcon";

    /// <summary>The resting opacity of an <c>idleFade</c> label row chip (All, a label, +N).</summary>
    public const string ChipKey = "Ab.IdleFadeChip";

    public const double IconRest = 0.22;

    public const double ChipRest = 0.30;

    /// <summary>Follows the setting: <paramref name="on"/> = the resting opacities, off = full strength.</summary>
    public static void Apply(bool on)
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        app.Resources[IconKey] = on ? IconRest : 1.0;
        app.Resources[ChipKey] = on ? ChipRest : 1.0;
    }

    /// <summary>Applies the store's value now and after every change.</summary>
    public static void Follow(TaskLayoutStore layout)
    {
        Apply(layout.Current.FadeIdleControls);
        layout.Changed += (_, _) => Apply(layout.Current.FadeIdleControls);
    }
}
