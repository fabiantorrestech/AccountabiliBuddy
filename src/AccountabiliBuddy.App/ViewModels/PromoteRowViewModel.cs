using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A site or app called on track in three sessions, offered once on the review card: always allow it?</summary>
public sealed partial class PromoteRowViewModel(RepeatAllow offer) : ObservableObject
{
    public RepeatAllow Offer { get; } = offer;

    public string Display => Offer.Display;

    /// <summary>E.g. "github.com: on track in 3 sessions. Always allow it?"</summary>
    public string Text => $"{Offer.Display}: on track in {RepeatAllows.DefaultThreshold} sessions. Always allow it?";

    /// <summary>Ticked: a permanent allow (needs the card's reason). Left: not added, and not offered again.</summary>
    [ObservableProperty]
    public partial bool IsYes { get; set; }
}
