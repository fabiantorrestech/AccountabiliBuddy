namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "+ New tag · Enter" box's place at the end of Move and label's tag chips (8c.56): one item of <see cref="MoveAndLabelViewModel.TagItems"/>,
/// so the box wraps with the chips; its text is the dialog's <see cref="MoveAndLabelViewModel.NewTagText"/>.
/// </summary>
public sealed class MoveAndLabelNewTagSlot
{
    public MoveAndLabelNewTagSlot(MoveAndLabelViewModel dialog)
    {
        Dialog = dialog;
    }

    public MoveAndLabelViewModel Dialog { get; }
}
