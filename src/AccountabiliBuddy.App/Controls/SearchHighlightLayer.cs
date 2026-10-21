using AccountabiliBuddy.App.Services.Search;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Lays translucent highlights over every place the search text shows on the current page (orange on the current
/// match, yellow on the rest), above the page and without taking clicks. Every shown text counts, also below the fold
/// (text blocks and the text in text boxes); only the part in view is drawn, clipped to the page. Scrolling just
/// redraws; new content (the scroll extent changing) or a new size recounts, at most every 120 ms.
/// <see cref="Next"/> moves to the next match and brings it into view.
/// </summary>
public sealed class SearchHighlightLayer : Control
{
    public static readonly StyledProperty<string?> QueryProperty =
        AvaloniaProperty.Register<SearchHighlightLayer, string?>(nameof(Query));

    public static readonly StyledProperty<Control?> PageProperty =
        AvaloniaProperty.Register<SearchHighlightLayer, Control?>(nameof(Page));

    public static readonly DirectProperty<SearchHighlightLayer, int> MatchCountProperty =
        AvaloniaProperty.RegisterDirect<SearchHighlightLayer, int>(nameof(MatchCount), layer => layer.MatchCount);

    private static readonly TimeSpan RecountDelay = TimeSpan.FromMilliseconds(120);
    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFA, 0xCC, 0x15));
    private static readonly IBrush CurrentBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFB, 0x92, 0x3C));

    private readonly DispatcherTimer _recount;
    private List<Match> _matches = new();
    private int _matchCount;

    public SearchHighlightLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _recount = new DispatcherTimer { Interval = RecountDelay };
        _recount.Tick += (_, _) =>
        {
            _recount.Stop();
            RecountNow();
        };
    }

    public string? Query
    {
        get => GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    /// <summary>The page being searched (the one showing).</summary>
    public Control? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>How many matches the page shows, in view or not.</summary>
    public int MatchCount
    {
        get => _matchCount;
        private set => SetAndRaise(MatchCountProperty, ref _matchCount, value);
    }

    /// <summary>The highlighted match (orange), counted from 0.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Moves to the next match (back to the first after the last) and brings it into view.</summary>
    public void Next()
    {
        if (_matches.Count == 0)
        {
            return;
        }

        ShowMatch((CurrentIndex + 1) % _matches.Count);
    }

    /// <summary>Moves to the previous match (to the last from the first) and brings it into view.</summary>
    public void Previous()
    {
        if (_matches.Count == 0)
        {
            return;
        }

        ShowMatch((CurrentIndex - 1 + _matches.Count) % _matches.Count);
    }

    private void ShowMatch(int index)
    {
        CurrentIndex = index;
        Match match = _matches[CurrentIndex];
        match.Owner.BringIntoView(match.LocalRect);
        InvalidateVisual();
    }

    /// <summary>Recounts soon: after a tab switch, new content or a new size.</summary>
    public void RecountSoon()
    {
        _recount.Stop();
        _recount.Start();
    }

    /// <summary>Finds every match in the page's shown texts, now.</summary>
    public void RecountNow()
    {
        var matches = new List<Match>();
        string query = Query ?? string.Empty;
        if (Page is { IsEffectivelyVisible: true } page && !string.IsNullOrWhiteSpace(query))
        {
            foreach (Visual visual in page.GetVisualDescendants())
            {
                if (TextOf(visual) is not (Control owner, string text, TextLayout layout, Thickness padding) || !IsShown(visual, page))
                {
                    continue;
                }

                foreach ((int start, int length) in TextMatcher.Find(text, query))
                {
                    foreach (Rect textRect in layout.HitTestTextRange(start, length))
                    {
                        matches.Add(new Match(owner, textRect.Translate(new Vector(padding.Left, padding.Top))));
                    }
                }
            }
        }

        _matches = matches;
        MatchCount = matches.Count;
        if (CurrentIndex >= matches.Count)
        {
            CurrentIndex = 0;
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            if (change.OldValue is Control oldPage)
            {
                oldPage.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnPageScrolled);
                oldPage.SizeChanged -= OnPageResized;
            }

            if (change.NewValue is Control newPage)
            {
                newPage.AddHandler(ScrollViewer.ScrollChangedEvent, OnPageScrolled, handledEventsToo: true);
                newPage.SizeChanged += OnPageResized;
            }

            CurrentIndex = 0;
            RecountNow();
        }
        else if (change.Property == QueryProperty)
        {
            CurrentIndex = 0;
            RecountNow();
        }
    }

    public override void Render(DrawingContext context)
    {
        for (int i = 0; i < _matches.Count; i++)
        {
            if (Visible(_matches[i]) is Rect shown)
            {
                context.DrawRectangle(i == CurrentIndex ? CurrentBrush : MatchBrush, null, shown, 2, 2);
            }
        }
    }

    private void OnPageScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return;
        }

        // New content changes the extent (e.g. another Settings group): recount; a plain scroll only moves the boxes.
        if (e.ExtentDelta != default || e.ViewportDelta != default)
        {
            RecountSoon();
        }
        else
        {
            InvalidateVisual();
        }
    }

    private void OnPageResized(object? sender, SizeChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Query))
        {
            RecountSoon();
        }
    }

    /// <summary>A text block's or a text box's text with its layout; null for anything else or an empty text.</summary>
    private static (Control Owner, string Text, TextLayout Layout, Thickness Padding)? TextOf(Visual visual)
    {
        return visual switch
        {
            TextBlock { Text: { Length: > 0 } text } block => (block, text, block.TextLayout, block.Padding),
            TextPresenter { Text: { Length: > 0 } text } presenter => (presenter, text, presenter.TextLayout, default(Thickness)),
            _ => null,
        };
    }

    /// <summary>
    /// True when the visual and every parent up to the page are visible. Checked by hand: a text in a group that was
    /// just hidden can still report itself effectively visible, with its old position.
    /// </summary>
    private static bool IsShown(Visual text, Visual page)
    {
        for (Visual? current = text; current is not null && !ReferenceEquals(current, page); current = current.GetVisualParent())
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The part of a match in view, on this layer: inside every scroll viewer around it and this layer.</summary>
    private Rect? Visible(Match match)
    {
        if (match.Owner.TranslatePoint(match.LocalRect.TopLeft, this) is not Point topLeft)
        {
            return null;
        }

        Rect shown = new Rect(topLeft, match.LocalRect.Size).Intersect(new Rect(Bounds.Size));
        for (ScrollViewer? scroller = match.Owner.FindAncestorOfType<ScrollViewer>(); scroller is not null; scroller = scroller.FindAncestorOfType<ScrollViewer>())
        {
            if (scroller.TranslatePoint(default, this) is Point scrollerTopLeft)
            {
                shown = shown.Intersect(new Rect(scrollerTopLeft, scroller.Bounds.Size));
            }
        }

        return shown.Width > 0 && shown.Height > 0 ? shown : null;
    }

    private sealed record Match(Control Owner, Rect LocalRect);
}
