using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One running wait on Today's banner: "Session ends in 0:42" with Cancel (ADR 66).</summary>
public sealed partial class RunningWaitRowViewModel : ObservableObject
{
    public RunningWaitRowViewModel(RunningWait wait, string text)
    {
        Wait = wait;
        Text = text;
    }

    public RunningWait Wait { get; private set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    public bool CanCancel => Wait.Cancel is not null;

    public void Update(RunningWait wait, string text)
    {
        Wait = wait;
        Text = text;
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (Wait.Cancel is Func<Task> cancel)
        {
            await cancel();
        }
    }
}
