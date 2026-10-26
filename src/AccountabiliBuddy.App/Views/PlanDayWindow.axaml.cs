using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>"Plan a day" (ADR 37); the calendar's picked dates go to the view model (they can't be bound).</summary>
public partial class PlanDayWindow : Window
{
    public PlanDayWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not PlanDayViewModel viewModel)
            {
                return;
            }

            viewModel.Done += (_, _) => Close();
            DatePicker.DisplayDateStart = viewModel.Today.ToDateTime(TimeOnly.MinValue);
            foreach (DateOnly date in viewModel.Dates)
            {
                DatePicker.SelectedDates.Add(date.ToDateTime(TimeOnly.MinValue));
            }

            DatePicker.SelectedDatesChanged += (_, _) =>
                viewModel.Dates = DatePicker.SelectedDates.Select(DateOnly.FromDateTime).ToList();
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
