using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One one-off block of the selected day in the Exceptions tab.</summary>
public sealed record OneOffBlockRowViewModel(OneOffBlock Block, string ProfileName)
{
    public string Text => $"{Block.Start:HH\\:mm}–{Block.End:HH\\:mm} · {ProfileName}";
}
