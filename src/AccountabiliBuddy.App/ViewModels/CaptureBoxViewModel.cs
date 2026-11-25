using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The capture line in Focus mode (redesign phase 5): Enter keeps a note for this session; with ⚑ on, or a leading "!",
/// it's a come-back instead, listed on Today until ticked. ⚑ turns itself off after each line. Since ADR 94 a note is a To sort task made
/// at once (<see cref="SessionNoteConversion.AddLineAsync"/>; the session's review still lists it); a note is kept only while the task list
/// can't be used.
/// </summary>
public sealed partial class CaptureBoxViewModel : ObservableObject
{
    private readonly SessionNotesStore _notes;
    private readonly ComebackStore _comebacks;
    private readonly SessionNoteConversion? _toSort;

    public CaptureBoxViewModel(SessionNotesStore notes, ComebackStore comebacks, SessionNoteConversion? toSort = null)
    {
        _notes = notes;
        _comebacks = comebacks;
        _toSort = toSort;
        Text = string.Empty;
    }

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>⚑: the next line is a come-back.</summary>
    [ObservableProperty]
    public partial bool IsComeback { get; set; }

    /// <summary>"Noted." or "Parked under Come back to."; empty until a line is kept.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [RelayCommand]
    private void Save()
    {
        string line = Text.Trim();
        if (line.Length == 0)
        {
            return;
        }

        bool comeback = IsComeback || line.StartsWith('!');
        string text = line.TrimStart('!').Trim();
        if (text.Length > 0)
        {
            _ = comeback ? _comebacks.AddAsync(text) : _toSort?.AddLineAsync(text) ?? _notes.AddAsync(text);
            Message = comeback ? "Parked under Come back to." : "Noted in To sort.";
        }

        Text = string.Empty;
        IsComeback = false;
    }
}
