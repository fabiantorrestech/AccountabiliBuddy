using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One time off entry of the selected day in the Exceptions tab.</summary>
public sealed record TimeOffRowViewModel(TimeOff TimeOff)
{
    public string Text
    {
        get
        {
            string hours = TimeOff.IsAllDay ? "All day" : $"{TimeOff.Start:HH\\:mm}–{TimeOff.End:HH\\:mm}";
            return string.IsNullOrWhiteSpace(TimeOff.Note) ? hours : $"{hours} · {TimeOff.Note}";
        }
    }
}
