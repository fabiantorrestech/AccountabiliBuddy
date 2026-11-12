namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// The PC's back / forward history (owner 2026-11-14), browser style: <see cref="Record"/> adds where the user went (and clears what
/// was ahead), <see cref="GoBack"/> / <see cref="GoForward"/> move through it. A place that only names more of the page already
/// current (Settings opening on its first section, Goals picking its first goal, a task opening on Tasks) replaces the current entry
/// instead of adding one. Places whose target is gone (a deleted task or goal) are skipped and dropped. At most
/// <see cref="Capacity"/> places are kept; the oldest go first. Memory only: nothing is stored. Use from the UI thread only.
/// </summary>
public sealed class NavigationHistory
{
    /// <summary>The most places kept.</summary>
    public const int Capacity = 50;

    private readonly List<NavigationPlace> _places = [];
    private int _index = -1;

    /// <summary>Raised whenever <see cref="CanGoBack"/>, <see cref="CanGoForward"/> or <see cref="Current"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The place shown now; null before the first one is recorded.</summary>
    public NavigationPlace? Current => _index >= 0 ? _places[_index] : null;

    public bool CanGoBack => _index > 0;

    public bool CanGoForward => _index >= 0 && _index < _places.Count - 1;

    /// <summary>How many places are kept (for the checks).</summary>
    public int Count => _places.Count;

    /// <summary>The user arrived at <paramref name="place"/> (not by back / forward): it becomes current and the forward places go.</summary>
    public void Record(NavigationPlace place)
    {
        if (Current is NavigationPlace current)
        {
            if (current == place)
            {
                return;
            }

            if (current.Tab == place.Tab && current.IsPageOnly && !place.IsPageOnly)
            {
                // More of the same page (its first section or goal, or the task just opened): the page's entry becomes this one.
                DropForward();
                _places[_index] = place;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        DropForward();
        _places.Add(place);
        if (_places.Count > Capacity)
        {
            _places.RemoveAt(0);
        }

        _index = _places.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The place before the current one that still exists, which becomes current; null when there is none (nothing moves then).
    /// Places passed over because they no longer exist are dropped.
    /// </summary>
    public NavigationPlace? GoBack(Func<NavigationPlace, bool> exists)
    {
        NavigationPlace? found = null;
        while (_index > 0)
        {
            NavigationPlace candidate = _places[_index - 1];
            if (exists(candidate))
            {
                _index--;
                found = candidate;
                break;
            }

            _places.RemoveAt(_index - 1);
            _index--;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return found;
    }

    /// <summary>The place after the current one that still exists, which becomes current; null when there is none.</summary>
    public NavigationPlace? GoForward(Func<NavigationPlace, bool> exists)
    {
        NavigationPlace? found = null;
        while (_index >= 0 && _index < _places.Count - 1)
        {
            NavigationPlace candidate = _places[_index + 1];
            if (exists(candidate))
            {
                _index++;
                found = candidate;
                break;
            }

            _places.RemoveAt(_index + 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return found;
    }

    private void DropForward()
    {
        if (_index >= 0 && _index < _places.Count - 1)
        {
            _places.RemoveRange(_index + 1, _places.Count - _index - 1);
        }
    }
}
