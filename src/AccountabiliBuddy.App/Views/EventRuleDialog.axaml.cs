using AccountabiliBuddy.Core.Domain;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>Asks for the words and category of a keyword rule made from a calendar event (ADR 64). Closes with true when saved.</summary>
public partial class EventRuleDialog : Window
{
    private readonly IReadOnlyList<DayCategory> _categories = [];

    public EventRuleDialog()
    {
        InitializeComponent();
    }

    /// <param name="keyword">The words suggested from the event's title.</param>
    /// <param name="categories">The categories to choose from.</param>
    public EventRuleDialog(string keyword, IReadOnlyList<DayCategory> categories)
        : this()
    {
        _categories = categories;
        KeywordBox.Text = keyword;
        CategoryBox.ItemsSource = categories.Select(category => category.Name).ToList();
        CategoryBox.SelectedIndex = categories.Count > 0 ? 0 : -1;
        Opened += (_, _) =>
        {
            KeywordBox.Focus();
            KeywordBox.SelectAll();
        };
        KeyDown += OnKeyPressed;
    }

    public string Keyword => KeywordBox.Text ?? string.Empty;

    public Guid? ChosenCategoryId => CategoryBox.SelectedIndex >= 0 && CategoryBox.SelectedIndex < _categories.Count ? _categories[CategoryBox.SelectedIndex].Id : null;

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Keyword) && ChosenCategoryId is not null)
        {
            Close(true);
        }
    }

    private void OnKeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }
}
