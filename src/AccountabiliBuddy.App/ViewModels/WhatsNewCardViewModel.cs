using AccountabiliBuddy.App.Services.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quiet Today card after an upgrade that brought something to set up ("New since your last version: 1 to set
/// up · Set up · Not now"); Not now hides it until the next version. The migration assistant itself is also behind
/// 5 taps on the About logo.
/// </summary>
public sealed partial class WhatsNewCardViewModel : ObservableObject
{
    private readonly SetupFeaturesService _features;
    private readonly Action _open;

    public WhatsNewCardViewModel(SetupFeaturesService features, Action open)
    {
        _features = features;
        _open = open;
        _features.Changed += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    public void Refresh()
    {
        IReadOnlyList<SetupFeature> news = _features.NewSinceUpgrade;
        IsVisible = _features.ShowsCard;
        Text = news.Count == 1
            ? $"New since your last version: {news[0].Title}."
            : $"New since your last version: {news.Count} things to set up.";
    }

    [RelayCommand]
    private void SetUp() => _open();

    [RelayCommand]
    private async Task NotNow() => await _features.DismissCardAsync();
}
