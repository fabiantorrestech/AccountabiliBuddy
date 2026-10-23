using AccountabiliBuddy.Core.Storage;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The app's animations: pages fade and slide in when their tab opens, cards ease their colour, progress bars glide to
/// their new value. One speed for all of them (Fast, Normal, Slow) or Off (document "motion"). Use from the UI thread.
/// </summary>
public sealed class Motion
{
    private const string DocumentKey = "motion";
    private static readonly TimeSpan PageEnter = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan Ease = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(450);

    private readonly SqliteJsonDocumentStore _store;
    private Styles? _styles;

    public Motion(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public MotionSettings Settings { get; private set; } = MotionSettings.Default;

    /// <summary>The app-wide motion currently in use (null before the app sets one up, e.g. in previews).</summary>
    public static Motion? Current { get; private set; }

    public bool IsOn => Settings.Speed != MotionSpeed.Off;

    public void LoadBlocking()
    {
        try
        {
            Settings = Task.Run(() => _store.LoadAsync<MotionSettings>(DocumentKey)).GetAwaiter().GetResult() ?? MotionSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = MotionSettings.Default;
        }

        Current = this;
    }

    public async Task SaveAsync(MotionSettings settings)
    {
        Settings = settings;
        Apply();
        await _store.SaveAsync(DocumentKey, settings);
    }

    /// <summary>A base duration at the chosen speed (zero when off).</summary>
    public TimeSpan Scale(TimeSpan normal) => Settings.Speed switch
    {
        MotionSpeed.Fast => normal * 0.6,
        MotionSpeed.Slow => normal * 1.7,
        MotionSpeed.Off => TimeSpan.Zero,
        _ => normal,
    };

    /// <summary>Puts the app-wide transitions in place for the current speed (or takes them away when off).</summary>
    public void Apply()
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        if (_styles is not null)
        {
            app.Styles.Remove(_styles);
            _styles = null;
        }

        if (!IsOn)
        {
            return;
        }

        try
        {
            AddStyles(app);
        }
        catch (Exception)
        {
            // Animations are a nicety: if the transitions can't be set up, the app runs without them.
            _styles = null;
        }
    }

    private void AddStyles(Application app)
    {
        var easing = new CubicEaseOut();
        _styles =
        [
            Transitions(x => x.OfType<Border>().Class("card"), new BrushTransition { Property = Border.BackgroundProperty, Duration = Scale(Ease), Easing = easing }),
            Transitions(x => x.OfType<ProgressBar>(), new DoubleTransition { Property = Avalonia.Controls.Primitives.RangeBase.ValueProperty, Duration = Scale(Glide), Easing = easing }),
        ];
        app.Styles.Add(_styles);
    }

    /// <summary>A page coming into view: a short fade and a lift of a few pixels.</summary>
    public async void Enter(Control page)
    {
        if (!IsOn)
        {
            page.Opacity = 1;
            return;
        }

        var lift = page.RenderTransform as TranslateTransform ?? new TranslateTransform();
        page.RenderTransform = lift;
        try
        {
            await Task.WhenAll(
                Fade(Visual.OpacityProperty, 0.0, 1.0).RunAsync(page),
                Fade(TranslateTransform.YProperty, 10.0, 0.0).RunAsync(lift));
        }
        catch (Exception)
        {
            // Never leave a page half-faded.
            page.Opacity = 1;
            lift.Y = 0;
        }
    }

    private Animation Fade(AvaloniaProperty property, double from, double to) => new()
    {
        Duration = Scale(PageEnter),
        Easing = new CubicEaseOut(),
        FillMode = FillMode.None,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
        },
    };

    private static Style Transitions(Func<Selector?, Selector> selector, ITransition transition)
    {
        var transitions = new Transitions { transition };
        return new Style(selector) { Setters = { new Setter(Animatable.TransitionsProperty, transitions) } };
    }
}
