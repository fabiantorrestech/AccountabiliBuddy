namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>Settings › Tasks › Labels › Label style: how a task row shows its label (spec 05 "Detail line order").</summary>
public enum TaskLabelStyle
{
    /// <summary>The label's dot and name at the row's right (the default).</summary>
    Dot,

    /// <summary>A tinted pill with the label's name before the title.</summary>
    PillBefore,

    /// <summary>A tinted pill with the label's name at the start of the detail line (not shown in Compact rows, which have no detail line).</summary>
    PillInDetails,
}
