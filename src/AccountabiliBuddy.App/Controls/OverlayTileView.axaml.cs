using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The overlay tile's body (ADR 51, boards 13a, 14a, 15b): the rounded border, the blocks in the order the settings give (session, timeline,
/// controls, clock, batteries, sound) and the outline for where it sits. The live overlay window and the Settings preview both host it; it
/// reads everything from the <see cref="TaskbarOverlayViewModel"/> it is given as its DataContext and knows nothing about windows.
/// </summary>
public partial class OverlayTileView : UserControl
{
    public OverlayTileView()
    {
        InitializeComponent();
    }

    /// <summary>Lays the blocks out for this target and settings, then shapes the outline. Call again whenever either changed.</summary>
    public void Apply(OverlayTarget target, OverlaySettings settings, double heightDips)
    {
        Rebuild(target, settings);
        ApplyShape(target, settings, heightDips);
    }

    private void Rebuild(OverlayTarget target, OverlaySettings settings)
    {
        MainRow.Children.Clear();
        ExtraRow.Children.Clear();
        MainRow.Orientation = target.Kind == OverlayTargetKind.VerticalTile ? Orientation.Vertical : Orientation.Horizontal;
        MainRow.HorizontalAlignment = target.Kind == OverlayTargetKind.VerticalTile ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        bool wide = settings.Size == OverlaySize.Wide;
        bool large = settings.Size == OverlaySize.Large && target.Kind != OverlayTargetKind.VerticalTile;
        bool ownClock = wide || target.CoversTray;
        bool sessionAdded = false;
        bool clockAdded = false;

        if (target.Kind == OverlayTargetKind.VerticalTile)
        {
            MainRow.Children.Add(Block("vertical"));
            ExtraRow.IsVisible = false;
            return;
        }

        foreach (OverlayItem item in settings.Items.Where(item => item.Enabled))
        {
            switch (item.Kind)
            {
                case OverlayItemKind.Ring or OverlayItemKind.Task when !sessionAdded:
                    MainRow.Children.Add(Block(settings.Size == OverlaySize.Small ? "sessionSmall" : "session"));
                    sessionAdded = true;
                    break;
                case OverlayItemKind.Timeline when large:
                    ExtraRow.Children.Add(Block("timeline"));
                    break;
                case OverlayItemKind.Timeline when wide:
                    MainRow.Children.Add(Block("timeline"));
                    break;
                case OverlayItemKind.Controls:
                    MainRow.Children.Add(Block("controls"));
                    break;
                case OverlayItemKind.TimeDate when ownClock:
                    MainRow.Children.Add(Block("clock"));
                    clockAdded = true;
                    break;
                case OverlayItemKind.Batteries when ownClock:
                    MainRow.Children.Add(Block("batteries"));
                    break;
                case OverlayItemKind.Sound when large:
                    ExtraRow.Children.Add(Block("sound"));
                    break;
                case OverlayItemKind.Sound when wide:
                    MainRow.Children.Add(Block("sound"));
                    break;
            }
        }

        if (target.CoversTray)
        {
            // It covers the tray, so it brings the clock and a ⌃ for the real overflow.
            if (!clockAdded)
            {
                MainRow.Children.Add(Block("clock"));
            }

            MainRow.Children.Add(Block("tray"));
        }

        ExtraRow.IsVisible = ExtraRow.Children.Count > 0;
    }

    private ContentControl Block(string key)
    {
        return new ContentControl { Content = DataContext, ContentTemplate = (IDataTemplate)Resources[key]!, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Docked: the corner next to the app icons is rounded, with a line on the top and that side. Floating and side tiles: rounded all round, or a pill.</summary>
    private void ApplyShape(OverlayTarget target, OverlaySettings settings, double heightDips)
    {
        Tile.Padding = new Thickness(10, 0);
        bool smallDocked = target.Kind == OverlayTargetKind.Docked && settings.Size == OverlaySize.Small;
        if (target.Kind == OverlayTargetKind.Docked && !smallDocked)
        {
            Tile.CornerRadius = target.AlignRight ? new CornerRadius(10, 0, 0, 0) : new CornerRadius(0, 10, 0, 0);
            Tile.BorderThickness = target.AlignRight ? new Thickness(1, 1, 0, 0) : new Thickness(0, 1, 1, 0);
            return;
        }

        Tile.BorderThickness = new Thickness(1);
        Tile.CornerRadius = settings.Shape == OverlayShape.Pill || target.Kind == OverlayTargetKind.VerticalTile
            ? new CornerRadius(Math.Min(heightDips, 999) / 2 > 40 ? 30 : heightDips / 2)
            : new CornerRadius(smallDocked ? 8 : 10);
        if (target.Kind == OverlayTargetKind.VerticalTile)
        {
            Tile.Padding = new Thickness(6, 8);
        }
    }
}