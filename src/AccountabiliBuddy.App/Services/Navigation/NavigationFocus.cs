namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>What has the keyboard when Alt+← / Alt+→ is pressed, as far as back / forward cares.</summary>
public enum NavigationFocus
{
    /// <summary>Nothing that uses Alt+← / Alt+→: the keys go back / forward.</summary>
    Other,

    /// <summary>A text box (or something inside one): the keys stay the box's.</summary>
    TextInput,

    /// <summary>A Tasks column header: Alt+← / Alt+→ move the column.</summary>
    ColumnHeader,
}
